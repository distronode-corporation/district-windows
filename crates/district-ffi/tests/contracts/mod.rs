//! Where the recorded server responses the projection tests answer with are:
//! the contract fixtures of District AI core for Rust, in the checkout of it
//! that Cargo.lock pins, beside its crates. The tests read the same bytes the
//! core's own contract tests decode, at the same commit, so a fixture is never
//! copied into this repository and never drifts from the one the core is held
//! to. The same reader as District AI for Linux's.

use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::OnceLock;

use serde::de::DeserializeOwned;

/// The core's `contracts/` directory. Cargo knows where it checked the core
/// out, so it is asked once (`cargo metadata`, offline and locked: the build
/// that compiled this test has already fetched that commit).
pub fn dir() -> &'static Path {
    static DIR: OnceLock<PathBuf> = OnceLock::new();
    DIR.get_or_init(|| {
        let workspace = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../Cargo.toml");
        let output = Command::new(env!("CARGO"))
            .args(["metadata", "--format-version", "1", "--locked", "--offline"])
            // This machine's dependencies only. Unfiltered, cargo resolves every
            // target's, and the Windows-only call engine's are not downloaded
            // on Linux, which offline is an error.
            .arg("--filter-platform")
            .arg(host())
            .arg("--manifest-path")
            .arg(&workspace)
            .output()
            .expect("cargo metadata did not start");
        assert!(
            output.status.success(),
            "cargo metadata failed: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        let metadata: serde_json::Value =
            serde_json::from_slice(&output.stdout).expect("cargo metadata printed no JSON");
        let model = metadata["packages"]
            .as_array()
            .expect("cargo metadata listed no packages")
            .iter()
            .find(|package| package["name"] == "district-model")
            .expect("district-model is not in the dependency graph");
        // <checkout>/crates/district-model/Cargo.toml
        let manifest = PathBuf::from(model["manifest_path"].as_str().expect("no manifest_path"));
        let dir = manifest
            .ancestors()
            .nth(3)
            .expect("district-model's manifest is not two directories deep")
            .join("contracts");
        assert!(
            dir.join("SHA256SUMS").is_file(),
            "{} holds no contract fixtures",
            dir.display()
        );
        dir
    })
}

/// The target triple of the toolchain running the tests (`rustc -vV`'s host).
fn host() -> String {
    let output = Command::new("rustc")
        .arg("-vV")
        .output()
        .expect("rustc did not start");
    String::from_utf8_lossy(&output.stdout)
        .lines()
        .find_map(|line| line.strip_prefix("host: "))
        .expect("rustc -vV named no host")
        .to_owned()
}

/// A recorded response from the core's `fixtures` set, as JSON to adjust.
pub fn json(name: &str) -> serde_json::Value {
    let file = dir().join("fixtures").join(name);
    let text = std::fs::read_to_string(&file)
        .unwrap_or_else(|error| panic!("cannot read {}: {error}", file.display()));
    serde_json::from_str(&text).unwrap_or_else(|error| panic!("{name}: {error}"))
}

/// A recorded response from the core's `fixtures` set, decoded.
pub fn read<T: DeserializeOwned>(name: &str) -> T {
    decode(name, json(name))
}

/// `value` decoded, or a failure naming `what`.
pub fn decode<T: DeserializeOwned>(what: &str, value: serde_json::Value) -> T {
    serde_json::from_value(value).unwrap_or_else(|error| panic!("{what}: {error}"))
}
