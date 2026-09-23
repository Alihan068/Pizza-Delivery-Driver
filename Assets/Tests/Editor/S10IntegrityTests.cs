using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Focused edit-mode contract tests for frozen S10 identity and submission gating.</summary>
public sealed class S10IntegrityTests {
    /// <summary>Verifies detached evidence copying and monotonic snapshot capture.</summary>
    [Test]
    public void ContentEvidenceAndSnapshot_CopyInputs_AndContextRejectsReplacement() {
        var modifierIds = new List<string>();
        var frozenModifiers = new FrozenModifierRules(null);
        var civilian = new List<SessionNpcProfileEvidence> {
            new SessionNpcProfileEvidence("civilian", 1f, 0.1f, 0.2f)
        };
        var evidence = CreateEvidence(modifierIds: modifierIds, civilianProfiles: civilian,
            resolvedModifiers: frozenModifiers);
        modifierIds.Add("changed");
        civilian.Clear();

        Assert.AreEqual(0, evidence.modifierIds.Count);
        Assert.AreEqual(1, evidence.civilianProfiles.Count);

        var draft = new SessionSetupDraft("session", "map", "vehicle", "normal", 3, false, null);
        var context = new TrafficSessionContext(draft, frozenModifiers);
        Assert.IsTrue(context.CaptureContentEvidence(evidence));
        Assert.IsFalse(context.CaptureContentEvidence(CreateEvidence()));
        context.FreezeSnapshot(new SessionRulesSnapshot("session", "map", "vehicle", "normal", 3,
            false, 0, evidence.modifierIds, 1f, true, true, "nav", evidence.modifiers, evidence));
        Assert.IsFalse(context.CaptureContentEvidence(CreateEvidence()));
    }

    /// <summary>Verifies frozen identity policy and trusted-manifest rejection states.</summary>
    [Test]
    public void CompetitiveRules_UseFrozenEvidence_AndRejectUntrustedVariants() {
        var provider = new FakeManifestProvider("manifest", "hash");
        Assert.AreEqual(CompetitiveEligibilityStatus.Eligible,
            CompetitiveRunRules.Evaluate(CreateEvidence(), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.CustomVehicleTuning,
            CompetitiveRunRules.Evaluate(CreateEvidence(customTuning: true), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.NonStandardDuration,
            CompetitiveRunRules.Evaluate(CreateEvidence(duration: 4), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.Unfinished,
            CompetitiveRunRules.Evaluate(CreateEvidence(), EndReason.Arrested, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.MissingTrustedManifest,
            CompetitiveRunRules.Evaluate(CreateEvidence(), EndReason.TimeUp, 3, null));
        Assert.AreEqual(CompetitiveEligibilityStatus.ContentIntegrityFailure,
            CompetitiveRunRules.Evaluate(CreateEvidence(actualHash: "wrong"), EndReason.TimeUp, 3, provider));
    }

    /// <summary>Verifies a captured identity mismatch is rejected without coalescing values.</summary>
    [Test]
    public void ContentEvidence_RejectsMismatchedDraftIdentity() {
        var frozenModifiers = new FrozenModifierRules(null);
        var evidence = CreateEvidence(resolvedModifiers: frozenModifiers);
        var mismatchedDraft = new SessionSetupDraft("session", "other-map", "vehicle", "normal", 3, false, null);

        Assert.IsFalse(evidence.MatchesSessionIdentity(mismatchedDraft, evidence.modifiers));
    }

    /// <summary>Verifies context capture and snapshot freezing reject mismatched detached evidence at the boundary.</summary>
    [Test]
    public void SessionContext_RejectsCaptureMismatch_AndSnapshotBypass() {
        var frozenModifiers = new FrozenModifierRules(null);
        var draft = new SessionSetupDraft("session", "map", "vehicle", "normal", 3, false, null);
        var context = new TrafficSessionContext(draft, frozenModifiers);
        var mismatchedEvidence = CreateEvidence(duration: 4, resolvedModifiers: frozenModifiers);

        Assert.IsFalse(context.CaptureContentEvidence(mismatchedEvidence));
        Assert.IsNull(context.ContentEvidence);

        var acceptedEvidence = CreateEvidence(resolvedModifiers: frozenModifiers);
        Assert.IsTrue(context.CaptureContentEvidence(acceptedEvidence));
        var bypass = new SessionRulesSnapshot("session", "other-map", "vehicle", "normal", 3,
            false, 0, frozenModifiers.Ids, 1f, true, true, "nav", frozenModifiers, acceptedEvidence);
        context.FreezeSnapshot(bypass);

        Assert.IsNull(context.Snapshot);
    }

    /// <summary>Verifies settlement carries invalid session integrity and the adapter blocks it before the sink.</summary>
    [Test]
    public void CompetitiveSubmission_RejectsInvalidSessionIntegrityBeforeSink() {
        var sink = new FakeCompetitiveResultSink();
        var adapter = new CompetitiveResultSubmissionAdapter(sink);
        var invalid = CompetitiveSubmissionRecord.CreateFromSettlement(CreateEvidence(), EndReason.TimeUp,
            321, 3, new FakeManifestProvider("manifest", "hash"), false, "navigation invalid");

        Assert.IsFalse(adapter.TrySubmit(new SessionResult { competitiveSubmission = invalid }));
        Assert.IsFalse(invalid.sessionIntegrityValid);
        Assert.AreEqual("navigation invalid", invalid.sessionIntegrityReason);
        Assert.AreEqual(0, sink.CallCount);
    }

    /// <summary>
    /// Verifies one final score and adapter-side rejection before sink invocation.
    /// </summary>
    [Test]
    public void CompetitiveRules_RejectCustomContentAndIncompleteCanonicalEvidence() {
        var provider = new FakeManifestProvider("manifest", "hash");
        Assert.AreEqual(CompetitiveEligibilityStatus.ExternalMap,
            CompetitiveRunRules.Evaluate(CreateEvidence(builtInMap: false), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.ExternalVehicle,
            CompetitiveRunRules.Evaluate(CreateEvidence(builtInVehicle: false), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.ContentIntegrityFailure,
            CompetitiveRunRules.Evaluate(CreateEvidence(navigationHash: string.Empty), EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.ContentIntegrityFailure,
            CompetitiveRunRules.Evaluate(CreateEvidence(civilianProfiles: new List<SessionNpcProfileEvidence>()),
                EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.ContentIntegrityFailure,
            CompetitiveRunRules.Evaluate(CreateEvidence(modifierIds: new List<string> { "different" }),
                EndReason.TimeUp, 3, provider));
        Assert.AreEqual(CompetitiveEligibilityStatus.ContentIntegrityFailure,
            CompetitiveRunRules.Evaluate(CreateEvidence(trustedProvenance: false), EndReason.TimeUp, 3, provider));
    }

    /// <summary>Verifies one final score is forwarded only for the validated fixture.</summary>
    [Test]
    public void CompetitiveSubmission_UsesOneFinalScore_AndSinkAcceptsOnlyValidatedFixture() {
        var sink = new FakeCompetitiveResultSink();
        var adapter = new CompetitiveResultSubmissionAdapter(sink);
        var valid = CompetitiveSubmissionRecord.CreateFromSettlement(CreateEvidence(), EndReason.TimeUp,
            321, 3, new FakeManifestProvider("manifest", "hash"), true);
        var validResult = new SessionResult { competitiveSubmission = valid };
        Assert.IsTrue(adapter.TrySubmit(validResult));
        Assert.AreEqual(321, valid.finalScore);

        CompetitiveEligibilityStatus[] rejected = {
            CompetitiveEligibilityStatus.CustomVehicleTuning,
            CompetitiveEligibilityStatus.NonStandardDuration,
            CompetitiveEligibilityStatus.Unfinished,
            CompetitiveEligibilityStatus.MissingTrustedManifest,
            CompetitiveEligibilityStatus.ContentIntegrityFailure
        };
        foreach (var status in rejected)
            Assert.IsFalse(adapter.TrySubmit(new SessionResult {
                competitiveSubmission = CompetitiveSubmissionRecord.CreateFromSettlement(
                    CreateEvidence(customTuning: status == CompetitiveEligibilityStatus.CustomVehicleTuning,
                        duration: status == CompetitiveEligibilityStatus.NonStandardDuration ? 4 : 3,
                        actualHash: status == CompetitiveEligibilityStatus.ContentIntegrityFailure ? "wrong" : "hash"),
                    status == CompetitiveEligibilityStatus.Unfinished ? EndReason.Arrested : EndReason.TimeUp,
                    321, 3,
                    status == CompetitiveEligibilityStatus.MissingTrustedManifest
                        ? null : new FakeManifestProvider("manifest", "hash"), true)
            }));
        Assert.IsFalse(adapter.TrySubmit(new SessionResult {
            competitiveSubmission = CompetitiveSubmissionRecord.CreateFromSettlement(CreateEvidence(),
                EndReason.Arrested, 321, 3, new FakeManifestProvider("manifest", "hash"), true)
        }));
        Assert.IsFalse(adapter.TrySubmit(new SessionResult()));
        Assert.IsFalse(adapter.TrySubmit(new SessionResult {
            competitiveSubmission = CompetitiveSubmissionRecord.CreateFromSettlement(
                CreateEvidence(navigationHash: string.Empty), EndReason.TimeUp, 321, 3,
                new FakeManifestProvider("manifest", "hash"), true)
        }));
        Assert.AreEqual(1, sink.CallCount);
        Assert.AreEqual(321, sink.LastFinalScore);
    }

    static SessionContentEvidence CreateEvidence(List<string> modifierIds = null,
        List<SessionNpcProfileEvidence> civilianProfiles = null, bool customTuning = false,
        int duration = 3, string actualHash = "hash", bool builtInMap = true,
        bool builtInVehicle = true, string navigationHash = "navigation-hash",
        bool trustedProvenance = true, FrozenModifierRules resolvedModifiers = null) {
        var modifiers = resolvedModifiers ?? new FrozenModifierRules(null);
        return new SessionContentEvidence("session", "map", "vehicle", "normal", duration, false,
            builtInMap, builtInVehicle, customTuning, true, true, true, true, "nav", "population", "police",
            SessionContentEvidence.ImplementedTrafficRulesetVersion, "manifest", actualHash, 1f, 0f, 0f, 0.1f, 100f, 15f, modifiers,
            modifierIds ?? new List<string>(),
            civilianProfiles ?? new List<SessionNpcProfileEvidence> {
                new SessionNpcProfileEvidence("civilian", 1f, 0f, 0f, 100f, "civilian-visual", "motor", null, "damage", "explosion")
            }, new List<SessionNpcProfileEvidence> {
                new SessionNpcProfileEvidence("police", 1f, 0f, 0f, 100f, "police-visual", "motor", "behavior", "damage", "explosion")
            }, navigationHash, "modifier-fingerprint", "population-fingerprint", "heat-tiers", trustedProvenance, trustedProvenance,
            trustedProvenance,
            20, 24, 10, 40);
    }

    sealed class FakeManifestProvider : ITrustedContentManifestProvider {
        readonly string manifestId;
        readonly string expectedHash;

        /// <summary>Creates deterministic expected-hash evidence for the fixture.</summary>
        public FakeManifestProvider(string manifestId, string expectedHash) {
            this.manifestId = manifestId;
            this.expectedHash = expectedHash;
        }

        /// <summary>Returns the fixture hash only for its exact manifest identity.</summary>
        public bool TryGetExpectedHash(string requestedId, out string hash) {
            hash = requestedId == manifestId ? expectedHash : null;
            return hash != null;
        }
    }

    sealed class FakeCompetitiveResultSink : ICompetitiveResultSink {
        int callCount;
        int lastFinalScore = -1;

        /// <summary>Number of records that passed the adapter.</summary>
        public int CallCount => callCount;
        /// <summary>Final score from the last forwarded record.</summary>
        public int LastFinalScore => lastFinalScore;

        /// <summary>Accepts every call so the production adapter is the only validation gate.</summary>
        public bool TrySubmit(CompetitiveSubmissionRecord record) {
            callCount++;
            lastFinalScore = record != null ? record.finalScore : -1;
            return true;
        }
    }
}
