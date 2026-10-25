using System.Text.Json.Nodes;
using DistrictAI.Core.Ffi;
using Xunit;

namespace DistrictAI.Core.Tests;

/// <summary>
/// The .NET scrub holds to the core's cases (district-crash's src/scrub.rs
/// tests), so a report from either side of the app leaves the same things out.
/// </summary>
public sealed class CrashScrubTests
{
    [Fact]
    public void LinuxMacosAndWindowsHomesLoseTheName()
    {
        Assert.Equal("/home/<user>/src/main.rs", CrashScrub.HomePaths("/home/alice/src/main.rs"));
        Assert.Equal("/Users/<user>/Library/x", CrashScrub.HomePaths("/Users/alice/Library/x"));
        Assert.Equal(@"C:\Users\<user>\AppData\x.dll", CrashScrub.HomePaths(@"C:\Users\alice\AppData\x.dll"));
        Assert.Equal("d:/users/<user>/x", CrashScrub.HomePaths("d:/users/alice/x"));
        Assert.Equal(@"\\?\C:\USERS\<user>\x", CrashScrub.HomePaths(@"\\?\C:\USERS\Alice Smith\x"));
        Assert.Equal(@"/Users\<user>\x", CrashScrub.HomePaths(@"/Users\alice\x"));
    }

    [Fact]
    public void EscapedAndDoubledSeparatorsLoseTheNameToo()
    {
        // A Rust `Debug`-formatted Windows path, as in an `unwrap` panic or an
        // error's derived `Debug`: every backslash is doubled.
        Assert.Equal(@"""C:\\Users\\<user>\\x""", CrashScrub.HomePaths(@"""C:\\Users\\alice\\x"""));
        Assert.Equal(
            @"Os { code: 5, path: ""C:\\Users\\<user>\\AppData"" }",
            CrashScrub.HomePaths(@"Os { code: 5, path: ""C:\\Users\\alice\\AppData"" }"));
        // JSON text held in a string escapes them the same way, and a string
        // escaped twice doubles them twice.
        Assert.Equal(@"{""path"":""c:\\users\\<user>\\x""}", CrashScrub.HomePaths(@"{""path"":""c:\\users\\Alice Smith\\x""}"));
        Assert.Equal(@"C:\\\\Users\\\\<user>\\\\x", CrashScrub.HomePaths(@"C:\\\\Users\\\\alice\\\\x"));
        // The verbatim prefix and a UNC share, plain and escaped.
        Assert.Equal(@"\\host\c$\Users\<user>\x", CrashScrub.HomePaths(@"\\host\c$\Users\alice\x"));
        Assert.Equal(@"\\\\?\\C:\\Users\\<user>\\x", CrashScrub.HomePaths(@"\\\\?\\C:\\Users\\alice\\x"));
        // Either slash, doubled or mixed, and a home at the very end.
        Assert.Equal("C:/Users/<user>/x", CrashScrub.HomePaths("C:/Users/alice/x"));
        Assert.Equal(@"C:\\Users/<user>\\x", CrashScrub.HomePaths(@"C:\\Users/alice\\x"));
        Assert.Equal("//Users//<user>", CrashScrub.HomePaths("//Users//alice"));
        Assert.Equal(@"C:\\Users\\<user>", CrashScrub.HomePaths(@"C:\\Users\\alice"));
    }

    [Fact]
    public void EveryHomeInASentenceAndOneAtTheVeryEnd()
    {
        Assert.Equal(
            "cannot open /home/<user>/a: no such file, nor /Users/<user>",
            CrashScrub.HomePaths("cannot open /home/bob/a: no such file, nor /Users/carol"));
        Assert.Equal("'/home/<user>' (from /home/<user>)", CrashScrub.HomePaths("'/home/bob' (from /home/eve)"));
        Assert.Equal(@"at ""C:\Users\<user>\x""", CrashScrub.HomePaths(@"at ""C:\Users\O'Brien (work)\x"""));
        Assert.Equal("a\t/home/<user>\tb", CrashScrub.HomePaths("a\t/home/bob\tb"));
        // Every character no name may hold ends one, and so do the controls.
        foreach (var end in "\"[]:;|=,+*?<>\x7f")
        {
            Assert.Equal($@"C:\Users\<user>{end}x", CrashScrub.HomePaths($@"C:\Users\alice{end}x"));
        }
    }

    [Fact]
    public void ANameKeepsCharactersOtherThanAscii()
    {
        Assert.Equal("/home/<user>/é", CrashScrub.HomePaths("/home/zoë/é"));
    }

    [Fact]
    public void TextWithoutAHomeComesBackUnchanged()
    {
        string[] texts =
        [
            "",
            "/",
            "/home/",
            "/home//x",
            "/usr/lib/x.so",
            "https://example.com/api/users/42",
            "/users/x",
            @"C:\Users",
            @"C:\\Users\\",
            @"C:\\Users\\\\",
            @"\\Users",
            "https://example.com//users//42",
            "https://users/42",
            @"C:\Program Files\x",
            "xUsersx",
            "Users/home",
            // Only ASCII is folded, as the core folds it: a long s is not an s.
            @"C:\Uſers\alice\x",
        ];
        foreach (var text in texts)
        {
            Assert.Same(text, CrashScrub.HomePaths(text));
        }
    }

    [Fact]
    public void AReportLosesItsHomesMachineUserAndRequest()
    {
        var report = JsonNode.Parse("""
            {
              "event_id": "0123456789abcdef0123456789abcdef",
              "server_name": "alices-laptop",
              "user": { "ip_address": "{{auto}}", "id": "installation" },
              "request": { "url": "https://example.com" },
              "message": { "formatted": "open /home/alice/notes.txt" },
              "exception": { "values": [ {
                "type": "System.IO.IOException",
                "value": "Could not find file 'C:\\Users\\alice\\AppData\\Local\\x'.",
                "stacktrace": { "frames": [ {
                  "filename": "/Users/alice/src/App.cs",
                  "abs_path": "C:\\Users\\alice\\src\\App.cs",
                  "function": "DistrictAI.App.OnLaunched",
                  "lineno": 12
                } ] }
              } ] },
              "contexts": { "device": { "name": "ALICES-PC", "arch": "x64" } },
              "extra": { "/home/alice/key": "/home/alice/value", "n": 1, "flag": true, "none": null },
              "tags": { "list": "/home/alice" }
            }
            """)!.AsObject();

        CrashScrub.Event(report);

        Assert.False(report.ContainsKey("server_name"));
        Assert.False(report.ContainsKey("user"));
        Assert.False(report.ContainsKey("request"));
        Assert.Equal("0123456789abcdef0123456789abcdef", (string?)report["event_id"]);
        Assert.Equal("open /home/<user>/notes.txt", (string?)report["message"]!["formatted"]);
        var exception = report["exception"]!["values"]![0]!;
        Assert.Equal(@"Could not find file 'C:\Users\<user>\AppData\Local\x'.", (string?)exception["value"]);
        var frame = exception["stacktrace"]!["frames"]![0]!;
        Assert.Equal("/Users/<user>/src/App.cs", (string?)frame["filename"]);
        Assert.Equal(@"C:\Users\<user>\src\App.cs", (string?)frame["abs_path"]);
        Assert.Equal("DistrictAI.App.OnLaunched", (string?)frame["function"]);
        Assert.Equal(12, (int?)frame["lineno"]);
        var device = report["contexts"]!["device"]!.AsObject();
        Assert.False(device.ContainsKey("name"));
        Assert.Equal("x64", (string?)device["arch"]);
        var extra = report["extra"]!.AsObject();
        Assert.Equal("/home/<user>/value", (string?)extra["/home/<user>/key"]);
        Assert.False(extra.ContainsKey("/home/alice/key"));
        Assert.Equal(1, (int?)extra["n"]);
        Assert.True((bool?)extra["flag"]);
        Assert.True(extra.ContainsKey("none"));
        Assert.Null(extra["none"]);
        Assert.Equal("/home/<user>", (string?)report["tags"]!["list"]);
    }

    /// <summary>
    /// The walk reads each string decoded, so a plain path in a field is one
    /// backslash per separator (above) whatever the report's JSON makes of it;
    /// what holds a doubled one is a string that itself holds escaped text,
    /// which is scrubbed as it is and stays escaped on the way out.
    /// </summary>
    [Fact]
    public void AReportHoldingEscapedPathsLosesTheNames()
    {
        var report = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["formatted"] = @"called `Result::unwrap()` on an `Err` value: ""C:\\Users\\alice\\x.toml""",
            },
            ["extra"] = new JsonObject { ["body"] = @"{""file"":""C:\\Users\\alice\\y""}" },
        };

        CrashScrub.Event(report);

        Assert.Equal(
            @"called `Result::unwrap()` on an `Err` value: ""C:\\Users\\<user>\\x.toml""",
            (string?)report["message"]!["formatted"]);
        Assert.Equal(@"{""file"":""C:\\Users\\<user>\\y""}", (string?)report["extra"]!["body"]);
        Assert.DoesNotContain("alice", report.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AReportWithNothingToScrubComesBackAsItWas()
    {
        const string Clean = """{"message":{"formatted":"nothing here"},"extra":{"n":1,"flag":true,"none":null,"list":["a","b",[1,{"k":"v"}]]},"contexts":{"os":{"name":"Windows"}}}""";
        var report = JsonNode.Parse(Clean)!.AsObject();
        CrashScrub.Event(report);
        Assert.Equal(Clean, report.ToJsonString());
    }

    [Fact]
    public void StringsInArraysAndKeysInNestedObjectsAreScrubbedToo()
    {
        var report = JsonNode.Parse("""{"breadcrumbs":[["/home/alice/a",{"/home/bob":["/Users/carol/x"]}]],"contexts":{"device":"not an object"}}""")!.AsObject();
        CrashScrub.Event(report);
        var inner = report["breadcrumbs"]![0]!.AsArray();
        Assert.Equal("/home/<user>/a", (string?)inner[0]);
        var nested = inner[1]!.AsObject();
        Assert.Equal("/home/<user>", Assert.Single(nested).Key);
        Assert.Equal("/Users/<user>/x", (string?)nested["/home/<user>"]![0]);
        Assert.Equal("not an object", (string?)report["contexts"]!["device"]);
    }

    [Fact]
    public void ItNeedsAReportAndText()
    {
        Assert.Throws<ArgumentNullException>(() => CrashScrub.Event(null!));
        Assert.Throws<ArgumentNullException>(() => CrashScrub.HomePaths(null!));
    }
}

/// <summary>The Rust side's crash reports, through the real district-ffi library.</summary>
public sealed class CrashReportsFfiTests
{
    private static CrashSettings Settings(string? dsn) => new(dsn, "com.distronode.DistrictAI@3.1.0+local", "production");

    /// <summary>
    /// A build with no DSN (every build but a release) sets nothing up,
    /// whatever the answer; without consent nothing is set up whatever the DSN.
    /// </summary>
    [Fact]
    public void NothingIsSetUpWithoutADsnOrWithoutConsent()
    {
        Assert.Equal(CrashStatus.OffNoDsn, DistrictFfi.CrashReportsApply(Settings(null), CrashConsent.Granted));
        Assert.False(DistrictFfi.CrashReportsEnabled());
        Assert.Equal(CrashStatus.OffNoConsent, DistrictFfi.CrashReportsApply(Settings("https://key@example.com/1"), CrashConsent.Denied));
        Assert.False(DistrictFfi.CrashReportsEnabled());
        Assert.True(DistrictFfi.CrashReportsFlush(10));
    }

    [Fact]
    public void ADsnItWillNotUseIsAnException()
    {
        Assert.Throws<CrashReportsException.InvalidDsn>(() => DistrictFfi.CrashReportsApply(Settings("not a dsn"), CrashConsent.Granted));
        Assert.Throws<CrashReportsException.InsecureDsn>(() => DistrictFfi.CrashReportsApply(Settings("http://key@example.com/1"), CrashConsent.Granted));
        Assert.False(DistrictFfi.CrashReportsEnabled());
    }
}
