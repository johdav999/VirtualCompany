using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserMeetingCaptureProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AgentGeneration",
                table: "sales_room_agent_transcripts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TranscriptSegmentId",
                table: "sales_room_agent_transcripts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_transcripts_CompanyId_TranscriptSegmentId",
                table: "sales_room_agent_transcripts",
                columns: new[] { "CompanyId", "TranscriptSegmentId" },
                unique: true,
                filter: "[TranscriptSegmentId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_room_agent_transcripts_sales_meeting_transcript_segments_CompanyId_TranscriptSegmentId",
                table: "sales_room_agent_transcripts",
                columns: new[] { "CompanyId", "TranscriptSegmentId" },
                principalTable: "sales_meeting_transcript_segments",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_room_agent_transcripts_sales_meeting_transcript_segments_CompanyId_TranscriptSegmentId",
                table: "sales_room_agent_transcripts");

            migrationBuilder.DropIndex(
                name: "IX_sales_room_agent_transcripts_CompanyId_TranscriptSegmentId",
                table: "sales_room_agent_transcripts");

            migrationBuilder.DropColumn(
                name: "AgentGeneration",
                table: "sales_room_agent_transcripts");

            migrationBuilder.DropColumn(
                name: "TranscriptSegmentId",
                table: "sales_room_agent_transcripts");
        }
    }
}
