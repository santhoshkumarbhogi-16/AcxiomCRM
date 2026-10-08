using AcxiomCRM.Data;
using AcxiomCRM.Models;
using System.Security.Claims;

namespace AcxiomCRM.Services;

public static class CrmRecordScope
{
    public static bool CanManageTeamRecords(this ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("Manager");

    public static IQueryable<Customer> VisibleCustomers(this ApplicationDbContext db, ClaimsPrincipal user)
    {
        if (user.CanManageTeamRecords())
            return db.Customers;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.Customers.Where(x => x.AssignedTo == userId);
    }

    public static IQueryable<Lead> VisibleLeads(this ApplicationDbContext db, ClaimsPrincipal user)
    {
        if (user.CanManageTeamRecords())
            return db.Leads;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.Leads.Where(x => x.AssignedTo == userId);
    }

    public static IQueryable<Opportunity> VisibleOpportunities(this ApplicationDbContext db, ClaimsPrincipal user)
    {
        if (user.CanManageTeamRecords())
            return db.Opportunities;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.Opportunities.Where(x => x.AssignedTo == userId);
    }

    public static IQueryable<FollowUp> VisibleFollowUps(this ApplicationDbContext db, ClaimsPrincipal user)
    {
        if (user.CanManageTeamRecords())
            return db.FollowUps;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.FollowUps.Where(x => x.AssignedTo == userId);
    }

    public static IQueryable<Activity> VisibleActivities(this ApplicationDbContext db, ClaimsPrincipal user)
    {
        if (user.CanManageTeamRecords())
            return db.Activities;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return db.Activities.Where(x => x.AssignedTo == userId);
    }
}
