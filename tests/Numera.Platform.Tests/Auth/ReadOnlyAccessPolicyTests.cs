using Microsoft.AspNetCore.Http;

using Numera.Api.Auth;
using Numera.Platform.Db.Entities;

using Xunit;

namespace Numera.Platform.Tests.Auth;

public sealed class ReadOnlyAccessPolicyTests
{
    [Theory]
    [InlineData("POST", "/api/documents")]
    [InlineData("PUT", "/api/partners")]
    [InlineData("PATCH", "/api/partners/0198/tasks")]
    [InlineData("DELETE", "/api/partners/0198/files")]
    [InlineData("POST", "/api/team")]
    [InlineData("PUT", "/api/catalog-items")]
    [InlineData("PATCH", "/api/company-profile")]
    [InlineData("DELETE", "/api/recurring-templates")]
    public void TaxAdvisor_denies_every_representative_unsafe_request(string method, string path)
    {
        Assert.False(ReadOnlyAccessPolicy.IsAllowed(method, new PathString(path), MembershipRole.TaxAdvisor));
    }

    [Fact]
    public void TaxAdvisor_allows_auth_write()
    {
        Assert.True(ReadOnlyAccessPolicy.IsAllowed(
            HttpMethods.Post,
            new PathString("/api/auth/logout"),
            MembershipRole.TaxAdvisor));
    }

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/documents/0198")]
    [InlineData("/api/documents/0198/pdf")]
    [InlineData("/api/documents/0198/xrechnung")]
    [InlineData("/api/open-items")]
    [InlineData("/api/inbound-documents")]
    [InlineData("/api/inbound-documents/0198/original")]
    [InlineData("/api/me")]
    [InlineData("/api/me/entitlements")]
    public void TaxAdvisor_allows_read_allow_list(string path)
    {
        Assert.True(ReadOnlyAccessPolicy.IsAllowed(HttpMethods.Get, new PathString(path), MembershipRole.TaxAdvisor));
    }

    [Theory]
    [InlineData("/api/partners")]
    [InlineData("/api/partners/0198/tasks")]
    [InlineData("/api/partners/0198/files")]
    [InlineData("/api/catalog-items")]
    [InlineData("/api/company-profile")]
    [InlineData("/api/dunning/config")]
    [InlineData("/api/recurring-templates")]
    [InlineData("/api/team")]
    [InlineData("/api/payments")]
    public void TaxAdvisor_denies_reads_outside_allow_list(string path)
    {
        Assert.False(ReadOnlyAccessPolicy.IsAllowed(HttpMethods.Get, new PathString(path), MembershipRole.TaxAdvisor));
    }

    [Theory]
    [InlineData(MembershipRole.Owner, "POST", "/api/partners")]
    [InlineData(MembershipRole.Employee, "DELETE", "/api/partners/0198/notes")]
    [InlineData(MembershipRole.Owner, "GET", "/api/partners")]
    [InlineData(MembershipRole.Employee, "GET", "/api/partners")]
    public void Owner_and_employee_are_not_restricted(
        MembershipRole role,
        string method,
        string path)
    {
        Assert.True(ReadOnlyAccessPolicy.IsAllowed(method, new PathString(path), role));
    }

    [Fact]
    public void Missing_role_is_not_restricted_by_this_policy()
    {
        Assert.True(ReadOnlyAccessPolicy.IsAllowed(
            HttpMethods.Delete,
            new PathString("/api/anything"),
            role: null));
    }
}
