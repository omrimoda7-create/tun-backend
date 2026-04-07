using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixMissingUserProfileMetadataColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Users`
                    ADD COLUMN IF NOT EXISTS `Age` int NULL,
                    ADD COLUMN IF NOT EXISTS `Gender` varchar(16) NOT NULL DEFAULT 'Male',
                    ADD COLUMN IF NOT EXISTS `AvatarUrl` varchar(255) NOT NULL DEFAULT '/b.png';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Users`
                    DROP COLUMN IF EXISTS `Age`,
                    DROP COLUMN IF EXISTS `Gender`,
                    DROP COLUMN IF EXISTS `AvatarUrl`;
                """);
        }
    }
}
