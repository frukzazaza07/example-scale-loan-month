using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LoanService.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Identity = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "failed_envelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Identity = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Queue = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplayedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failed_envelopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "journals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Identity = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "loans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Customer = table.Column<string>(type: "text", nullable: false),
                    OpeningPrincipal = table.Column<long>(type: "bigint", nullable: false),
                    OutstandingPrincipal = table.Column<long>(type: "bigint", nullable: false),
                    AnnualRate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    MonthlyPayment = table.Column<long>(type: "bigint", nullable: false),
                    PaidInterest = table.Column<long>(type: "bigint", nullable: false),
                    UnappliedCredit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loans", x => x.Id);
                    table.CheckConstraint("loan_balances", "\"OutstandingPrincipal\" >= 0 AND \"OutstandingPrincipal\" <= \"OpeningPrincipal\" AND \"UnappliedCredit\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Queue = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Identity = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankId = table.Column<string>(type: "text", nullable: false),
                    BankTransactionId = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    OriginalReference = table.Column<string>(type: "text", nullable: false),
                    ResolvedReference = table.Column<string>(type: "text", nullable: true),
                    LoanId = table.Column<long>(type: "bigint", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    BankOutcome = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    Envelope = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LateReview = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "review_conflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankTransactionId = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_conflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "text", nullable: false),
                    Debit = table.Column<long>(type: "bigint", nullable: false),
                    Credit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_postings", x => x.Id);
                    table.CheckConstraint("posting_positive", "\"Debit\" >= 0 AND \"Credit\" >= 0 AND (\"Debit\" = 0 OR \"Credit\" = 0)");
                    table.ForeignKey(
                        name: "FK_postings_journals_JournalId",
                        column: x => x.JournalId,
                        principalTable: "journals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LoanId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledOpeningPrincipal = table.Column<long>(type: "bigint", nullable: false),
                    Interest = table.Column<long>(type: "bigint", nullable: false),
                    Principal = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledClosingPrincipal = table.Column<long>(type: "bigint", nullable: false),
                    PaidInterest = table.Column<long>(type: "bigint", nullable: false),
                    PaidPrincipal = table.Column<long>(type: "bigint", nullable: false),
                    InterestRecognized = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installments", x => x.Id);
                    table.CheckConstraint("installment_paid", "\"PaidInterest\" >= 0 AND \"PaidInterest\" <= \"Interest\" AND \"PaidPrincipal\" >= 0 AND \"PaidPrincipal\" <= \"Principal\"");
                    table.ForeignKey(
                        name: "FK_installments_loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallmentId = table.Column<long>(type: "bigint", nullable: false),
                    InstallmentNumber = table.Column<int>(type: "integer", nullable: false),
                    Interest = table.Column<long>(type: "bigint", nullable: false),
                    Principal = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_allocations_payments_AppliedPaymentId",
                        column: x => x.AppliedPaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_allocations_AppliedPaymentId",
                table: "allocations",
                column: "AppliedPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_failed_envelopes_Identity",
                table: "failed_envelopes",
                column: "Identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installments_LoanId_Number",
                table: "installments",
                columns: new[] { "LoanId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journals_Identity",
                table: "journals",
                column: "Identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_loans_Reference",
                table: "loans",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_PublishedAt_AvailableAt",
                table: "outbox",
                columns: new[] { "PublishedAt", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_BankId_BankTransactionId",
                table: "payments",
                columns: new[] { "BankId", "BankTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_postings_JournalId",
                table: "postings",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_review_conflicts_BankTransactionId_EventId",
                table: "review_conflicts",
                columns: new[] { "BankTransactionId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocations");

            migrationBuilder.DropTable(
                name: "audits");

            migrationBuilder.DropTable(
                name: "failed_envelopes");

            migrationBuilder.DropTable(
                name: "installments");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "postings");

            migrationBuilder.DropTable(
                name: "review_conflicts");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "loans");

            migrationBuilder.DropTable(
                name: "journals");
        }
    }
}
