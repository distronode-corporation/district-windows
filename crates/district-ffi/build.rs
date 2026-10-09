//! Only for the `scripted` feature: copies the core's contract fixtures into
//! `OUT_DIR`, where `src/scripted.rs` embeds the ones it answers with. A build
//! without the feature does nothing here, so the Store build embeds none.
//!
//! The fixtures are the core's own (`contracts/fixtures/` in the checkout of
//! district-core-rust that Cargo.lock pins), the same bytes the projection
//! tests read, so nothing is copied into this repository. The checkout is
//! found as Cargo leaves it, under `$CARGO_HOME/git/checkouts/`, by the commit
//! Cargo.lock records; `DISTRICT_CORE_CONTRACTS` names another `contracts/`
//! directory (a vendored build, say).

use std::path::{Path, PathBuf};

fn main() {
    println!("cargo::rerun-if-changed=build.rs");
    println!("cargo::rerun-if-env-changed=DISTRICT_CORE_CONTRACTS");
    if std::env::var_os("CARGO_FEATURE_SCRIPTED").is_none() {
        return;
    }
    let contracts = contracts_dir();
    let from = contracts.join("fixtures");
    let to = PathBuf::from(std::env::var_os("OUT_DIR").expect("no OUT_DIR")).join("fixtures");
    std::fs::create_dir_all(&to).expect("cannot make OUT_DIR/fixtures");
    let entries = std::fs::read_dir(&from)
        .unwrap_or_else(|error| panic!("cannot read {}: {error}", from.display()));
    for entry in entries {
        let path = entry.expect("cannot list the fixtures").path();
        if path
            .extension()
            .is_some_and(|extension| extension == "json")
        {
            println!("cargo::rerun-if-changed={}", path.display());
            let name = path.file_name().expect("a fixture has a name");
            std::fs::copy(&path, to.join(name))
                .unwrap_or_else(|error| panic!("cannot copy {}: {error}", path.display()));
        }
    }
}

/// The core's `contracts/` directory.
fn contracts_dir() -> PathBuf {
    if let Some(dir) = std::env::var_os("DISTRICT_CORE_CONTRACTS") {
        return PathBuf::from(dir);
    }
    let manifest =
        PathBuf::from(std::env::var_os("CARGO_MANIFEST_DIR").expect("no CARGO_MANIFEST_DIR"));
    let lock_file = manifest.join("../../Cargo.lock");
    println!("cargo::rerun-if-changed={}", lock_file.display());
    let lock = std::fs::read_to_string(&lock_file)
        .unwrap_or_else(|error| panic!("cannot read {}: {error}", lock_file.display()));
    let commit = core_commit(&lock).expect("Cargo.lock has no git source for district-model");
    let checkouts = cargo_home().join("git").join("checkouts");
    let found = std::fs::read_dir(&checkouts)
        .unwrap_or_else(|error| panic!("cannot read {}: {error}", checkouts.display()))
        .filter_map(Result::ok)
        .filter(|entry| {
            entry
                .file_name()
                .to_string_lossy()
                .starts_with("district-core-rust-")
        })
        .map(|entry| entry.path().join(&commit[..7]).join("contracts"))
        .find(|dir| dir.join("fixtures").is_dir());
    found.unwrap_or_else(|| {
        panic!(
            "no checkout of district-core-rust at {} under {}; build once without the feature, or set DISTRICT_CORE_CONTRACTS",
            &commit[..7],
            checkouts.display()
        )
    })
}

/// The commit Cargo.lock pins district-model at: the part after `#` of its
/// `source = "git+...#<commit>"` line.
fn core_commit(lock: &str) -> Option<String> {
    let mut in_model = false;
    for line in lock.lines() {
        if line.starts_with("[[package]]") {
            in_model = false;
        } else if line == "name = \"district-model\"" {
            in_model = true;
        } else if in_model && let Some(source) = line.strip_prefix("source = \"") {
            let commit = source.trim_end_matches('"').rsplit_once('#')?.1;
            return (commit.len() >= 7).then(|| commit.to_owned());
        }
    }
    None
}

fn cargo_home() -> PathBuf {
    if let Some(home) = std::env::var_os("CARGO_HOME") {
        return PathBuf::from(home);
    }
    let user = std::env::var_os("USERPROFILE")
        .or_else(|| std::env::var_os("HOME"))
        .expect("neither CARGO_HOME nor a home directory is set");
    Path::new(&user).join(".cargo")
}
