using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpandUserAvatarUrlToLongText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Users`
                    MODIFY COLUMN `AvatarUrl` longtext NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Users`
                    MODIFY COLUMN `AvatarUrl` varchar(255) NOT NULL DEFAULT '/b.png';
                """);
        }
    }
}
