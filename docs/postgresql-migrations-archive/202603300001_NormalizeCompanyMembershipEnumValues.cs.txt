using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class NormalizeCompanyMembershipEnumValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE company_memberships
            SET Role = CASE Role
                WHEN 'Owner' THEN 'owner'
                WHEN 'Admin' THEN 'admin'
                WHEN 'Manager' THEN 'manager'
                WHEN 'Employee' THEN 'employee'
                WHEN 'FinanceApprover' THEN 'finance_approver'
                WHEN 'SupportSupervisor' THEN 'support_supervisor'
                ELSE Role
            END,
            Status = CASE Status
                WHEN 'Pending' THEN 'pending'
                WHEN 'Active' THEN 'active'
                WHEN 'Revoked' THEN 'revoked'
                ELSE Status
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE company_memberships
            SET Role = CASE Role
                WHEN 'owner' THEN 'Owner'
                WHEN 'admin' THEN 'Admin'
                WHEN 'manager' THEN 'Manager'
                WHEN 'employee' THEN 'Employee'
                WHEN 'finance_approver' THEN 'FinanceApprover'
                WHEN 'support_supervisor' THEN 'SupportSupervisor'
                ELSE Role
            END,
            Status = CASE Status
                WHEN 'pending' THEN 'Pending'
                WHEN 'active' THEN 'Active'
                WHEN 'revoked' THEN 'Revoked'
                ELSE Status
            END;
            """);
    }
}