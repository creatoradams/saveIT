using System.Globalization;
using Amazon.Runtime;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Protected temporary-session carrier, not a credential provider chain or secret-manager client.
internal sealed class ComputeAwsSession
{
    internal SessionAWSCredentials Credentials { get; }
    internal DateTimeOffset ExpiresAt { get; }
    private ComputeAwsSession(string access,string secret,string token,DateTimeOffset expires)
    {Credentials=new SessionAWSCredentials(access,secret,token);ExpiresAt=expires;}
    internal void RequireFresh()
    {if(ExpiresAt<=DateTimeOffset.UtcNow.AddMinutes(1))throw new InvalidOperationException();}
    internal static async Task<ComputeAwsSession> ReadAsync(string path,Func<string,string?> environment,CancellationToken ct)
    {
        foreach(var name in ForbiddenEnvironment)if(!string.IsNullOrEmpty(environment(name)))throw new InvalidOperationException();
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(OperatingSystem.IsLinux())
        {
            var mode=File.GetUnixFileMode(file.SafeFileHandle);
            if((mode&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0 || (mode&UnixFileMode.UserRead)==0)throw new InvalidOperationException();
        }
        if(file.Length is <1 or >32768)throw new InvalidOperationException();
        return await ReadAsync(file,ct);
    }
    internal static Task<ComputeAwsSession> ReadAsync(Stream input,CancellationToken ct)=>ProtectedJsonInput.ReadAsync(input,4,root=> {
        RejectDuplicates(root);var fields=Object(root,"accessKeyId","secretAccessKey","sessionToken","expiration");
        string Value(string name)=>fields[name].GetString()??throw new InvalidOperationException();
        var access=Value("accessKeyId");var secret=Value("secretAccessKey");var token=Value("sessionToken");
        Match(access,"ASIA[A-Z0-9]{16}");Match(secret,"[A-Za-z0-9/+=]{40}");
        if(token.Length is <16 or >16384 || token.Any(c=>c<=32||c>=127)||
            !DateTimeOffset.TryParseExact(Value("expiration"),["yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var expires)||
            expires<=DateTimeOffset.UtcNow.AddMinutes(1)||expires>DateTimeOffset.UtcNow.AddHours(1))throw new InvalidOperationException();
        return new ComputeAwsSession(access,secret,token,expires);
    },ct,32768);
    private static readonly string[] ForbiddenEnvironment=[
        "AWS_ACCESS_KEY_ID","AWS_SECRET_ACCESS_KEY","AWS_SESSION_TOKEN","AWS_PROFILE","AWS_DEFAULT_PROFILE","AWS_SHARED_CREDENTIALS_FILE","AWS_CONFIG_FILE",
        "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI","AWS_CONTAINER_CREDENTIALS_FULL_URI","AWS_CONTAINER_AUTHORIZATION_TOKEN","AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE",
        "AWS_WEB_IDENTITY_TOKEN_FILE","AWS_ROLE_ARN","AWS_ENDPOINT_URL","AWS_ENDPOINT_URL_ECS","AWS_ENDPOINT_URL_STS","AWS_ENDPOINT_URL_ECR","AWS_ENDPOINT_URL_SECRETS_MANAGER","AWS_CA_BUNDLE",
        "AWS_EC2_METADATA_SERVICE_ENDPOINT","AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE"
    ];
}
