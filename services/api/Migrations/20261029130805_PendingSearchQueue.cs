using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class PendingSearchQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enqueue in the source transaction: an event committed late cannot be skipped by a checkpoint.
            // The API cannot write consumer receipts; this fixed trigger only inserts NEW's own composite key.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.enqueue_search_delivery() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
                BEGIN
                    INSERT INTO public."SearchDeliveries" ("TenantId","EventId","Attempts","NextAttemptAt")
                    VALUES (NEW."TenantId",NEW."Id",0,CURRENT_TIMESTAMP)
                    ON CONFLICT ("TenantId","EventId") DO NOTHING;
                    RETURN NEW;
                END $$;
                REVOKE ALL ON FUNCTION public.enqueue_search_delivery() FROM PUBLIC;
                CREATE TRIGGER enqueue_search_delivery AFTER INSERT ON public."OutboxEvents"
                    FOR EACH ROW EXECUTE FUNCTION public.enqueue_search_delivery();
                INSERT INTO public."SearchDeliveries" ("TenantId","EventId","Attempts","NextAttemptAt")
                    SELECT "TenantId","Id",0,CURRENT_TIMESTAMP FROM public."OutboxEvents"
                    ON CONFLICT ("TenantId","EventId") DO NOTHING;
                REVOKE DELETE ON public."SearchDeliveries" FROM saveit_search_worker;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_SearchDeliveries_Due",
                table: "SearchDeliveries",
                columns: new[] { "TenantId", "NextAttemptAt", "EventId" },
                filter: "\"CompletedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER enqueue_search_delivery ON public."OutboxEvents";
                DROP FUNCTION public.enqueue_search_delivery();
                GRANT DELETE ON public."SearchDeliveries" TO saveit_search_worker;
                """);
            migrationBuilder.DropIndex(
                name: "IX_SearchDeliveries_Due",
                table: "SearchDeliveries");
        }
    }
}
