using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeModerationResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModerationResults_Messages_MessageId",
                table: "ModerationResults");

            migrationBuilder.DropIndex(
                name: "IX_ModerationResults_MessageId",
                table: "ModerationResults");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "ModerationResults",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentId",
                table: "ModerationResults",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ContentSnapshot",
                table: "ModerationResults",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ModerationResults",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FlagsData",
                table: "ModerationResults",
                type: "varchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ModerationResults",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.Sql(
                """
                UPDATE ModerationResults mr
                INNER JOIN Messages m ON m.Id = mr.MessageId
                SET
                    mr.ContentId = mr.MessageId,
                    mr.ContentType = 'Message',
                    mr.ContentSnapshot = m.Content,
                    mr.UserId = m.UserId;
                """);

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "ModerationResults");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationResults_ContentType_ContentId",
                table: "ModerationResults",
                columns: new[] { "ContentType", "ContentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationResults_UserId_CreatedAtUtc",
                table: "ModerationResults",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_ModerationResults_Users_UserId",
                table: "ModerationResults",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModerationResults_Users_UserId",
                table: "ModerationResults");

            migrationBuilder.DropIndex(
                name: "IX_ModerationResults_ContentType_ContentId",
                table: "ModerationResults");

            migrationBuilder.DropIndex(
                name: "IX_ModerationResults_UserId_CreatedAtUtc",
                table: "ModerationResults");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "ModerationResults",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "ModerationResults",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.Sql(
                """
                UPDATE ModerationResults
                SET MessageId = ContentId
                WHERE ContentType = 'Message';
                """);

            migrationBuilder.DropColumn(
                name: "ContentId",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "ContentSnapshot",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "FlagsData",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ModerationResults");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationResults_MessageId",
                table: "ModerationResults",
                column: "MessageId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ModerationResults_Messages_MessageId",
                table: "ModerationResults",
                column: "MessageId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
