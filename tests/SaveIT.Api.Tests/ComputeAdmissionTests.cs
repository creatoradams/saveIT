using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Deployment;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("compute")]
public sealed class ComputeAdmissionTests(DatabaseFixture database,ComputeReleaseFixture signing) : IAsyncLifetime,IClassFixture<ComputeReleaseFixture>
{
    private readonly string login="saveit_release_test_"+Guid.NewGuid().ToString("N");
    private string Connection=>new NpgsqlConnectionStringBuilder(database.OwnerConnection){Username=login,Password="synthetic-release-test-only",Pooling=false}.ConnectionString;
    public async Task InitializeAsync()
    {
        await database.Execute($"CREATE ROLE {login} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-release-test-only'; GRANT saveit_compute_operator TO {login}");
        Assert.Equal(1,await Change(0,"initialize"));
    }
    public Task DisposeAsync()=>database.Execute($"DELETE FROM public.\"ComputeOperations\";DELETE FROM public.\"ComputeControl\";DROP ROLE {login}");
    private Task<long> Change(long expected,string action)=>ComputeRunner.ChangeAsync(Connection,JsonSerializer.Serialize(new ComputeChange(expected,Guid.NewGuid(),action,null,null,"synthetic-release-operator"),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}),true);
    private Task<ComputeRelease> Release(long expected=1,Guid? operation=null,Guid? generation=null,long? expires=null)=>signing.CreateAsync(expected,operation,generation,expires);
    private Task<long> Admit(ComputeRelease release,string? connection=null)=>ComputeRunner.AdmitReleaseAsync(connection??Connection,release,"synthetic-release-operator",true);
    private async Task<JsonDocument> Status(Guid operation)=>JsonDocument.Parse(await ComputeRunner.InspectReleaseAsync(Connection,operation,true));
    private async Task WaitFor(Func<Task<bool>> condition)
    {using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!await condition())await Task.Delay(20,deadline.Token);}
    private async Task<long> RawAdmit(ComputeRelease r,long? expected=null,Guid? operation=null,Guid? generation=null,string? hash=null,string? key=null)
    {
        await using var c=new NpgsqlConnection(Connection);await c.OpenAsync();await using(var role=new NpgsqlCommand("SET ROLE saveit_compute_operator",c))await role.ExecuteNonQueryAsync();
        await using var command=new NpgsqlCommand("SELECT public.compute_admit_v1(@v,@i,@g,@p,@k,@j,'synthetic-direct-operator')",c);
        command.Parameters.AddWithValue("v",expected??r.Data.ExpectedVersion);command.Parameters.AddWithValue("i",operation??r.OperationId);command.Parameters.AddWithValue("g",generation??Guid.Parse(r.Data.Generation));command.Parameters.AddWithValue("p",hash??r.PlanSha256);command.Parameters.AddWithValue("k",key??r.KeySha256);command.Parameters.AddWithValue("j",r.SignedToken);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Verified_intent_and_exact_signed_binding_commit_atomically_with_body_free_operation_inspection()
    {
        var r=await Release();Assert.Equal(2,await Admit(r));using var status=await Status(r.OperationId);var s=status.RootElement;
        Assert.True(s.GetProperty("admitted").GetBoolean());Assert.Equal(2,s.GetProperty("admittedVersion").GetInt64());Assert.Equal(r.PlanSha256,s.GetProperty("planSha256").GetString());Assert.Equal(r.KeySha256,s.GetProperty("keySha256").GetString());Assert.Equal("Starting",s.GetProperty("currentPhase").GetString());Assert.True(s.GetProperty("currentGenerationMatches").GetBoolean());
        Assert.DoesNotContain(r.SignedToken,s.GetRawText());Assert.DoesNotContain("okta",s.GetRawText());Assert.DoesNotContain("contract",s.GetRawText());
        Assert.Equal(r.SignedToken,await database.Scalar<string>($"SELECT \"ReleaseJws\" FROM public.\"ComputeOperations\" WHERE \"OperationId\"='{r.OperationId}'"));
        Assert.Equal(1L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\" WHERE \"ReleaseJws\" IS NOT NULL"));
        await Change(2,"activate");using var after=await Status(r.OperationId);Assert.Equal("Running",after.RootElement.GetProperty("currentPhase").GetString());Assert.Equal(2,after.RootElement.GetProperty("admittedVersion").GetInt64());
        using var missing=await Status(Guid.NewGuid());Assert.False(missing.RootElement.GetProperty("admitted").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_admissions_have_exactly_one_winner_without_discarding_the_loser_outcome()
    {
        var a=await Release();var b=await Release();
        async Task<bool> Attempt(ComputeRelease r){try{await Admit(r);return true;}catch(PostgresException e){Assert.Equal("40001",e.SqlState);return false;}}
        var results=await Task.WhenAll(Attempt(a),Attempt(b));Assert.Single(results,x=>x);
        using var sa=await Status(a.OperationId);using var sb=await Status(b.OperationId);Assert.NotEqual(sa.RootElement.GetProperty("admitted").GetBoolean(),sb.RootElement.GetProperty("admitted").GetBoolean());
        Assert.Equal(2L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
    }

    [Fact]
    public async Task Replayed_approval_operation_and_generation_cannot_be_reused_after_parking()
    {
        var first=await Release();await Admit(first);await Change(2,"quiesce");await Change(3,"drained");await Change(4,"fencing");await Change(5,"park");
        Assert.Equal("40001",(await Assert.ThrowsAsync<PostgresException>(()=>Admit(first))).SqlState);
        var newGenerationSameOperation=await Release(6,first.OperationId);Assert.Equal("23505",(await Assert.ThrowsAsync<PostgresException>(()=>Admit(newGenerationSameOperation))).SqlState);
        var oldGeneration=await Release(6,generation:Guid.Parse(first.Data.Generation));Assert.Equal("22023",(await Assert.ThrowsAsync<PostgresException>(()=>Admit(oldGeneration))).SqlState);
        Assert.Equal(6L,await database.Scalar<long>("SELECT \"Version\" FROM public.\"ComputeControl\""));Assert.Equal("Parked",await database.Scalar<string>("SELECT \"Phase\" FROM public.\"ComputeControl\""));
        Assert.Equal(1L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\" WHERE \"ReleaseJws\" IS NOT NULL"));
    }

    [Fact]
    public async Task Checked_SQL_refuses_digest_key_operation_generation_and_expected_version_binding_mistakes()
    {
        var r=await Release();
        foreach(var call in new Func<Task<long>>[]{()=>RawAdmit(r,hash:new string('0',64)),()=>RawAdmit(r,key:new string('0',64)),()=>RawAdmit(r,operation:Guid.NewGuid()),()=>RawAdmit(r,generation:Guid.NewGuid()),()=>RawAdmit(r,expected:2)})
            Assert.Equal("22023",(await Assert.ThrowsAsync<PostgresException>(call)).SqlState);
        Assert.Equal(1L,await database.Scalar<long>("SELECT \"Version\" FROM public.\"ComputeControl\""));Assert.Equal(1L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
    }

    [Fact]
    public async Task Approval_expiring_during_real_global_gate_wait_rolls_back_post_wait_admission()
    {
        await using var held=new NpgsqlConnection(database.OwnerConnection);await held.OpenAsync();await using var tx=await held.BeginTransactionAsync();
        await using(var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(91555333526868::bigint)",held,tx))await gate.ExecuteNonQueryAsync();
        var release=await Release(expires:DateTimeOffset.UtcNow.ToUnixTimeSeconds()+2);var admission=Admit(release);
        try {
            await WaitFor(()=>database.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.compute_admit_v1%' AND wait_event='PgSleep')"));Assert.False(admission.IsCompleted);
            await WaitFor(()=>Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds()>=release.ExpiresAt));
            await tx.CommitAsync();Assert.Equal("22023",(await Assert.ThrowsAsync<PostgresException>(()=>admission)).SqlState);
        }
        finally {await tx.DisposeAsync();try{await admission;}catch(Exception){}}
        Assert.Equal(1L,await database.Scalar<long>("SELECT \"Version\" FROM public.\"ComputeControl\""));using var status=await Status(release.OperationId);Assert.False(status.RootElement.GetProperty("admitted").GetBoolean());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Receipt_statement_or_deferred_commit_failure_rolls_back_the_control_and_journal_together(bool deferred)
    {
        var r=await Release();const string name="compute_admission_failure";
        await database.Execute($"CREATE FUNCTION public.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"ReleaseJws\" IS NOT NULL THEN RAISE EXCEPTION 'synthetic admission failure';END IF;RETURN NEW;END $$;CREATE {(deferred?"CONSTRAINT ":"")}TRIGGER {name} {(deferred?"AFTER":"BEFORE")} UPDATE ON public.\"ComputeOperations\" {(deferred?"DEFERRABLE INITIALLY DEFERRED ":"")}FOR EACH ROW EXECUTE FUNCTION public.{name}()");
        try{await Assert.ThrowsAsync<PostgresException>(()=>Admit(r));Assert.Equal(1L,await database.Scalar<long>("SELECT \"Version\" FROM public.\"ComputeControl\""));Assert.Equal(1L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));}
        finally{await database.Execute($"DROP TRIGGER {name} ON public.\"ComputeOperations\";DROP FUNCTION public.{name}()");}
    }

    [Fact]
    public async Task Lost_COMMIT_ack_keeps_exact_signed_admission_inspectable_and_never_retries()
    {
        var r=await Release();await using var proxy=new AccessCommitProxy(Connection,1);
        await Assert.ThrowsAnyAsync<NpgsqlException>(()=>Admit(r,proxy.Connection));await proxy.CommitLost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var status=await Status(r.OperationId);Assert.True(status.RootElement.GetProperty("admitted").GetBoolean());Assert.Equal(r.PlanSha256,status.RootElement.GetProperty("planSha256").GetString());
        Assert.Equal("40001",(await Assert.ThrowsAsync<PostgresException>(()=>Admit(r))).SqlState);Assert.Equal(2L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
    }

    [Theory]
    [InlineData("valid")] [InlineData("private-drift")] [InlineData("held-input")]
    public async Task Actual_stdin_CLI_records_only_verified_intent_with_exact_output_and_no_ambient_PGOPTIONS_authority(string mode)
    {
        var r=await Release(generation:Guid.Parse("70000000-0000-0000-0000-000000000001"));
        var observations=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-observations.json")))!;
        if(mode=="private-drift")observations["web"]!["taskDefinition"]!["containerDefinitions"]![0]!["environment"]!.AsArray().Add(new JsonObject{["name"]="ConnectionStrings__App",["value"]="synthetic-private-marker"});
        var path=Path.GetTempFileName();await File.WriteAllBytesAsync(path,signing.PublicKey);
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(typeof(ComputeRunner).Assembly.Location);start.ArgumentList.Add("compute");start.ArgumentList.Add("admit-release");
        foreach(var (name,value) in new Dictionary<string,string>{["SAVEIT_COMPUTE_ACCOUNT_ID"]="111122223333",["SAVEIT_COMPUTE_REGION"]="us-east-1",["SAVEIT_COMPUTE_CELL"]="pilot",["SAVEIT_COMPUTE_STAGE"]="staging",["SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE"]=path,["SAVEIT_COMPUTE_RELEASE_KEY_SHA256"]=signing.Pin,["SAVEIT_COMPUTE_CONNECTION"]=Connection,["SAVEIT_COMPUTE_OPERATOR"]="synthetic-cli-operator",["SAVEIT_COMPUTE_ALLOW_LOOPBACK"]="true",["PGOPTIONS"]="-c role=saveit_migration_owner",["AWS_PROFILE"]="synthetic-private-marker"})start.Environment[name]=value;
        start.Environment.Remove("Deployment__Trust__DatabaseCaSha256");
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(new JsonObject{["signedRelease"]=r.SignedToken,["observations"]=observations}.ToJsonString());await process.StandardInput.FlushAsync();if(mode!="held-input")process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(27));
            Assert.Equal(mode=="valid"?0:1,process.ExitCode);
            Assert.Equal(mode=="valid"?"SaveIT compute release intent committed at version 2 (supplied observations; no provider actions).":"",(await output).Trim());
            Assert.Equal(mode=="valid"?"":"SaveIT deployment operation failed. Check the protected configuration, role, schema and operation prerequisites.",(await errors).Trim());
            using var status=await Status(r.OperationId);Assert.Equal(mode=="valid",status.RootElement.GetProperty("admitted").GetBoolean());
        }
        finally{process.StandardInput.Close();if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}File.Delete(path);}
    }

    [Theory]
    [InlineData("saveit_app")] [InlineData("saveit_search_worker")] [InlineData("saveit_access_operator")]
    public async Task Other_roles_cannot_execute_admission_or_inspection_read_payload_columns_or_mutate_receipts(string role)
    {
        var r=await Release();await Admit(r);await using var c=new NpgsqlConnection(database.OwnerConnection);await c.OpenAsync();await using(var set=new NpgsqlCommand("SET ROLE "+role,c))await set.ExecuteNonQueryAsync();
        foreach(var sql in new[]{"SELECT public.compute_admit_v1(NULL,NULL,NULL,NULL,NULL,NULL,NULL)",$"SELECT public.compute_release_status_v1('{r.OperationId}')",$"SELECT public.compute_release_v1('{r.OperationId}')","SELECT \"ReleaseJws\" FROM public.\"ComputeOperations\"","UPDATE public.\"ComputeOperations\" SET \"ReleaseKeySha256\"=NULL","DELETE FROM public.\"ComputeOperations\""})
        {await using var forbidden=new NpgsqlCommand(sql,c);Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>forbidden.ExecuteNonQueryAsync())).SqlState);}
    }

    [Theory]
    [InlineData("compute_admit_v1(bigint,uuid,uuid,text,text,text,text)")]
    [InlineData("compute_release_status_v1(uuid)")] [InlineData("compute_release_v1(uuid)")]
    public async Task PUBLIC_and_NO_INHERIT_function_grants_fail_runtime_and_access_operator_guards(string function)
    {
        var accessLogin="release_access_"+Guid.NewGuid().ToString("N");
        await database.Execute($"CREATE ROLE {accessLogin} LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-access-test-only';GRANT saveit_access_operator TO {accessLogin}");
        var accessConnection=new NpgsqlConnectionStringBuilder(Connection){Username=accessLogin,Password="synthetic-access-test-only"}.ConnectionString;
        async Task GuardsFail()
        {
            foreach(var connection in new[]{database.AppConnection,database.WorkerConnection})
            {await using var app=new NpgsqlConnection(connection);await app.OpenAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseRoleGuard.ValidateAsync(app));}
            var manifest=JsonSerializer.Serialize(new AccessInspection(DevelopmentSeed.TenantA,DevelopmentSeed.Profiles.Single(x=>x.Id=="alice").MembershipId,"synthetic-release-inspection"));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>AccessRunner.InspectAsync(accessConnection,manifest,true));
        }
        try {
        await database.Execute($"GRANT EXECUTE ON FUNCTION public.{function} TO PUBLIC");
        try{await GuardsFail();}
        finally{await database.Execute($"REVOKE EXECUTE ON FUNCTION public.{function} FROM PUBLIC");}
        var bridge="release_bridge_"+Guid.NewGuid().ToString("N");await database.Execute($"CREATE ROLE {bridge} NOLOGIN NOINHERIT;GRANT {bridge} TO saveit_app,saveit_search_worker,{accessLogin};GRANT EXECUTE ON FUNCTION public.{function} TO {bridge}");
        try{await GuardsFail();}
        finally{await database.Execute($"REVOKE EXECUTE ON FUNCTION public.{function} FROM {bridge};REVOKE {bridge} FROM saveit_app,saveit_search_worker,{accessLogin};DROP ROLE {bridge}");}
        }
        finally{await database.Execute($"DROP ROLE {accessLogin}");}
    }

    [Fact]
    public async Task Downgrade_removes_release_surface_preserves_prior_core_control_and_upgrade_invents_no_approval()
    {
        var r=await Release();await Admit(r);
        await using var db=new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(database.OwnerConnection).Options,new());var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261109120000_ComputeCoordination");
        try{Assert.True(await database.Scalar<bool>("SELECT to_regprocedure('public.compute_release_v1(uuid)') IS NULL"));Assert.Equal(2L,await database.Scalar<long>("SELECT \"Version\" FROM public.\"ComputeControl\""));Assert.Equal("Starting",await database.Scalar<string>("SELECT \"Phase\" FROM public.\"ComputeControl\""));}
        finally{await migrator.MigrateAsync(DatabaseRoleGuard.RequiredMigration);}
        using var status=await Status(r.OperationId);Assert.False(status.RootElement.GetProperty("admitted").GetBoolean());Assert.Equal(2L,await database.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
    }
}
