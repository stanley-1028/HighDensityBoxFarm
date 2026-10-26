// ============================================================================
// HDBF v2 | Layer: L1 test | Owner: V2-GATE-01
// Purpose: NFR-GATE-01 (REQ-V2-READ-01/READ-05, NFR-01): the docs/v2 set exists,
//          every file carries the section-19.18.1-A header block (status/date/
//          applicable version/change summary), all four mandatory ADRs are present,
//          TRACEABILITY.md covers every REQ-V2-* id of the requirement document and
//          no unregistered TODO/TBD placeholder is left. Read-only file IO through
//          the HdbfRepoRoot assembly metadata; no game/Unity/network dependency.
// Requirements: REQ-V2-READ-01, REQ-V2-READ-05, REQ-V2-NFR-01, REQ-V2-NFR-03, REQ-V2-TEST-01
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace HDBF.Tests
{
    public static class V2NfrDocumentationTests
    {
        private const string RepoRootKey = "HdbfRepoRoot";
        private const string RequirementsDocument = "docs/REQ_V2_SECTION_19.md";

        /// <summary>Document set of section 19.18.1-A (NFR-GATE-01).</summary>
        private static readonly string[] DocumentSet =
        {
            "README.md",
            "ARCHITECTURE.md",
            "INTERFACES.md",
            "ADRS.md",
            "TRACEABILITY.md",
            "CHANGELOG.md",
        };

        /// <summary>docs/VERIFICATION.md belongs to the same set but is the Lead's evidence file.</summary>
        private const string VerificationDocument = "docs/VERIFICATION.md";

        private static readonly string[] ChineseHeaderLabels = { "状态", "日期", "适用版本", "变更摘要" };

        private static readonly string[] EnglishHeaderLabels =
        {
            "Status", "Date", "Applicable version", "Change summary",
        };

        /// <summary>Unregistered placeholder markers forbidden in the released documentation set.</summary>
        private static readonly string[] ForbiddenPlaceholders = { "TODO", "TBD", "待补", "<placeholder>" };

        /// <summary>Markers a document is allowed to *document* (the convention line lists all four).</summary>
        private static readonly string[] ChangeMarkers = { "【新增】", "【修改】", "【作废】", "【待确认】" };

        private static readonly Regex RequirementIdPattern =
            new Regex(@"REQ-V2-[A-Z]+-\d+", RegexOptions.Compiled);

        private static readonly Regex RequirementRangePattern =
            new Regex(@"REQ-V2-([A-Z]+)-(\d+)\.\.(\d+)", RegexOptions.Compiled);

        private static readonly Regex AdrHeadingPattern =
            new Regex(@"^##\s+ADR-V2-\d{3}\b", RegexOptions.Compiled | RegexOptions.Multiline);

        // ------------------------------------------------------------------ NFR-GATE-01: set

        public static void TestDocumentSetExistsAndIsNotEmpty()
        {
            var descriptions = new List<string>();
            foreach (var name in DocumentSet)
            {
                var path = DocPath(name);
                TestAssert.True(File.Exists(path), "missing v2 document: docs/v2/" + name);
                var info = new FileInfo(path);
                TestAssert.True(info.Length > 1000,
                    "docs/v2/" + name + " is only " + info.Length + " bytes; an empty skeleton is not releasable");
                var text = ReadDocument(name);
                var lines = SplitLines(text);
                TestAssert.True(lines.Length >= 10,
                    "docs/v2/" + name + " has only " + lines.Length + " lines; expected a real document");
                TestAssert.True(lines[0].StartsWith("# ", StringComparison.Ordinal),
                    "docs/v2/" + name + " must start with a markdown title");
                descriptions.Add(name + " (" + lines.Length + " lines)");
            }

            var verification = RepoPath(VerificationDocument);
            TestAssert.True(File.Exists(verification),
                "the v2 documentation set of section 19.18.1-A includes docs/VERIFICATION.md");
            TestAssert.True(new FileInfo(verification).Length > 1000,
                "docs/VERIFICATION.md must carry real evidence, not an empty placeholder");

            Console.WriteLine("[nfr-gate-01] document set: " + string.Join(", ", descriptions)
                              + "; plus docs/VERIFICATION.md");
        }

        // ------------------------------------------------------------------ NFR-GATE-01: headers

        public static void TestEveryDocumentCarriesTheRequiredHeaderBlock()
        {
            foreach (var name in DocumentSet)
            {
                var text = ReadDocument(name);
                var lines = SplitLines(text);
                var chinese = ContainsCjk(text);
                var labels = chinese ? ChineseHeaderLabels : EnglishHeaderLabels;
                var region = HeaderRegion(lines, 30);

                foreach (var label in labels)
                {
                    var found = false;
                    for (var index = 0; index < region.Length; index++)
                    {
                        if (region[index].Contains(label, StringComparison.Ordinal))
                        {
                            found = true;
                            // The label must be followed by a value, not left as an empty skeleton.
                            var valueStart = region[index].IndexOf(label, StringComparison.Ordinal) + label.Length;
                            var value = region[index].Substring(valueStart).Trim(' ', '*', '：', ':', '｜', '|', '-');
                            TestAssert.True(value.Length >= 2,
                                "docs/v2/" + name + " header field '" + label + "' has no value");
                            break;
                        }
                    }

                    TestAssert.True(found,
                        "docs/v2/" + name + " header must contain '" + label
                        + "' within the first 30 lines (section 19.18.1-A)");
                }
            }

            Console.WriteLine("[nfr-gate-01] every document carries 状态/日期/适用版本/变更摘要");
        }

        // ------------------------------------------------------------------ NFR-GATE-01: ADRs

        public static void TestAdrSetContainsTheFourMandatoryDecisions()
        {
            var adrs = ReadDocument("ADRS.md");
            var headings = AdrHeadingPattern.Matches(adrs);
            TestAssert.True(headings.Count >= 4,
                "docs/v2/ADRS.md must contain at least four ADR sections; found " + headings.Count);

            var seen = new List<string>();
            for (var number = 1; number <= 4; number++)
            {
                var id = "ADR-V2-" + number.ToString("000", CultureInfo.InvariantCulture);
                var heading = FindHeading(adrs, id);
                TestAssert.True(heading.Length > 0,
                    "docs/v2/ADRS.md is missing the mandatory decision " + id
                    + " (section 19.18.1-A: indoor farm / flat roof stacking / save v2 / water model)");
                var section = SectionBody(adrs, heading);
                TestAssert.True(section.Length >= 3,
                    id + " must not be a stub; the decision, rationale and cost are required");
                TestAssert.True(section.Contains("决策", StringComparison.Ordinal)
                               || section.Contains("Decision", StringComparison.OrdinalIgnoreCase),
                    id + " must record an explicit decision (决策/Decision)");
                seen.Add(id);
            }

            TestAssert.True(headings.Count >= seen.Count, "ADR heading parse mismatch");
            Console.WriteLine("[nfr-gate-01] ADRs present: " + string.Join(", ", seen)
                              + " plus " + (headings.Count - seen.Count) + " additional record(s)");
        }

        // ------------------------------------------------------------------ NFR-GATE-01: traceability

        public static void TestTraceabilityCoversEveryRequirementId()
        {
            var requirementText = RepoRead(RequirementsDocument);
            var required = ExtractRequirementIds(requirementText);
            TestAssert.True(required.Count >= 60,
                "the requirement document (§19) yielded only " + required.Count
                + " REQ-V2-* ids; the parse is probably broken");

            var traceability = ReadDocument("TRACEABILITY.md");
            var covered = ExtractRequirementIds(traceability);

            var missing = new List<string>();
            foreach (var id in required)
            {
                if (!covered.Contains(id))
                {
                    missing.Add(id);
                }
            }

            missing.Sort(StringComparer.Ordinal);
            TestAssert.Equal(0, missing.Count,
                "docs/v2/TRACEABILITY.md does not cover " + missing.Count + " requirement id(s): "
                + string.Join(", ", missing));
            TestAssert.True(traceability.Contains("|", StringComparison.Ordinal),
                "TRACEABILITY.md must be a matrix (markdown table rows)");

            // READ-05: the change record must reference REQ ids as well (CHANGELOG or its ADR siblings).
            var changeIds = new HashSet<string>(ExtractRequirementIds(ReadDocument("CHANGELOG.md")), StringComparer.Ordinal);
            foreach (var id in ExtractRequirementIds(ReadDocument("ADRS.md")))
            {
                changeIds.Add(id);
            }

            TestAssert.True(changeIds.Count >= 4,
                "CHANGELOG/ADRS must reference the v2 requirement ids (REQ-V2-READ-05); found "
                + changeIds.Count);
            TestAssert.True(ReadDocument("CHANGELOG.md").Contains("【新增】", StringComparison.Ordinal)
                           && ReadDocument("CHANGELOG.md").Contains("【修改】", StringComparison.Ordinal),
                "CHANGELOG.md must use the 【新增】/【修改】 change markers of section 19.18.1-A");

            Console.WriteLine("[nfr-gate-01] traceability covers " + required.Count + "/" + required.Count
                              + " REQ-V2 ids; change records reference " + changeIds.Count + " ids");
        }

        // ------------------------------------------------------------------ NFR-GATE-01: placeholders

        public static void TestDocumentSetHasNoUnregisteredPlaceholders()
        {
            var scanned = 0;
            foreach (var name in DocumentSet)
            {
                scanned += ScanForPlaceholders("docs/v2/" + name, ReadDocument(name));
            }

            scanned += ScanForPlaceholders(VerificationDocument, RepoRead(VerificationDocument));

            Console.WriteLine("[nfr-gate-01] no unregistered TODO/TBD/待补/<placeholder>/【待确认】 in "
                              + scanned + " document(s)");
        }

        // ------------------------------------------------------------------ helpers

        private static int ScanForPlaceholders(string label, string text)
        {
            var lines = SplitLines(text);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (IsChangeMarkerConventionLine(line))
                {
                    // The document-set README documents the 【新增】/【修改】/【作废】/【待确认】
                    // marker vocabulary in one line; that is the convention, not a placeholder.
                    continue;
                }

                foreach (var token in ForbiddenPlaceholders)
                {
                    TestAssert.False(line.Contains(token, StringComparison.Ordinal),
                        label + ":" + (index + 1) + " contains the unregistered placeholder '" + token + "'");
                }

                // 【待确认】 is allowed only in the requirement document sections 19.15/19.16
                // (still-open decisions), never in the released v2 document set.
                TestAssert.False(line.Contains("【待确认】", StringComparison.Ordinal),
                    label + ":" + (index + 1)
                    + " contains 【待确认】; section 19.18.1-A only allows that marker for the open items of §19.15/§19.16");
            }

            return 1;
        }

        private static bool IsChangeMarkerConventionLine(string line)
        {
            var markers = 0;
            foreach (var marker in ChangeMarkers)
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                {
                    markers++;
                }
            }

            return markers >= 3;
        }

        private static HashSet<string> ExtractRequirementIds(string text)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in RequirementIdPattern.Matches(text))
            {
                ids.Add(match.Value);
            }

            // §19.17 and the NFR tables abbreviate ranges ("REQ-V2-SAVE-04..07"); a matrix that
            // lists the range covers every id inside it, so expand both documents the same way.
            foreach (Match match in RequirementRangePattern.Matches(text))
            {
                var domain = match.Groups[1].Value;
                var from = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var to = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                for (var number = from; number <= to; number++)
                {
                    ids.Add("REQ-V2-" + domain + "-" + number.ToString("00", CultureInfo.InvariantCulture));
                }
            }

            return ids;
        }

        private static string FindHeading(string text, string id)
        {
            var lines = SplitLines(text);
            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal) && line.Contains(id, StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return string.Empty;
        }

        private static string SectionBody(string text, string heading)
        {
            var lines = SplitLines(text);
            var start = Array.IndexOf(lines, heading);
            if (start < 0)
            {
                return string.Empty;
            }

            var body = new StringBuilder();
            for (var index = start + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("# ", StringComparison.Ordinal))
                {
                    break;
                }

                body.AppendLine(line);
            }

            return body.ToString();
        }

        private static string[] HeaderRegion(string[] lines, int count)
        {
            var length = Math.Min(count, lines.Length);
            var region = new string[length];
            Array.Copy(lines, region, length);
            return region;
        }

        private static bool ContainsCjk(string text)
        {
            foreach (var character in text)
            {
                if (character >= '\u4E00' && character <= '\u9FFF')
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static string DocPath(string name)
        {
            return RepoPath("docs/v2/" + name);
        }

        private static string ReadDocument(string name)
        {
            return File.ReadAllText(DocPath(name), Encoding.UTF8);
        }

        private static string RepoRead(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath), Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var metadata = typeof(V2NfrDocumentationTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var attribute in metadata)
            {
                if (string.Equals(attribute.Key, RepoRootKey, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(attribute.Value))
                {
                    return attribute.Value!;
                }
            }

            throw new TestFailureException("missing HdbfRepoRoot assembly metadata");
        }
    }
}
