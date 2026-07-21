// Stub ReflectionVerifier for EvalEngine integration testing
// Supports preset BusinessVerificationReport for predictable test behavior

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;

namespace Agent1.Tests.Stubs;

public class StubReflectionVerifier : ReflectionVerifier
{
    private BusinessVerificationReport? _presetReport;

    public StubReflectionVerifier(IKnowledgeBaseService kbService)
        : base(kbService)
    {
    }

    /// <summary>Set the report returned by VerifyBusinessFactsAsync</summary>
    public void SetBusinessVerificationReport(BusinessVerificationReport report)
        => _presetReport = report;

    /// <summary>Create and set a simple report with given claims</summary>
    public void SetPresetClaims(List<ClaimVerification> claims)
    {
        var report = new BusinessVerificationReport { Claims = claims };
        int verified = claims.Count(c => c.FoundInSource);
        report.FactualPrecision = claims.Count > 0 ? (double)verified / claims.Count : 1.0;
        foreach (var c in claims)
            if (!c.FoundInSource)
                report.HallucinatedClaims.Add(c.ClaimedText);
        _presetReport = report;
    }

    public override async Task<BusinessVerificationReport> VerifyBusinessFactsAsync(string conclusion)
    {
        if (_presetReport != null)
        {
            _presetReport.RawConclusion = conclusion;
            return _presetReport;
        }
        return await base.VerifyBusinessFactsAsync(conclusion);
    }
}
