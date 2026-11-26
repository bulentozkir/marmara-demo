using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetCoreSqlDb.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoSearchQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows and older app instances must receive distinct search keys.
            migrationBuilder.AddColumn<Guid>(
                name: "SearchDocumentId",
                table: "Todo",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.CreateTable(
                name: "SearchOperations",
                columns: table => new
                {
                    ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchOperations", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Todo_SearchDocumentId",
                table: "Todo",
                column: "SearchDocumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchOperations");

            migrationBuilder.DropIndex(
                name: "IX_Todo_SearchDocumentId",
                table: "Todo");

            migrationBuilder.DropColumn(
                name: "SearchDocumentId",
                table: "Todo");
        }
    }
}
