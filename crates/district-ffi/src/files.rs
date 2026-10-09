//! Files: picking, reading and attaching them (a reply's attachments, the help
//! desk's logo).
//!
//! C# opens the file chooser with what [`attachment_pick`] or [`logo_pick`]
//! asks for, reads each file picked into a [`PickedFileView`] (no further than
//! [`FilePickView::read_cap`]), and the area turns it into the core's
//! [`PickedAttachment`] with [`picked_attachment`]. A file that could not be
//! read at all is not a `PickedFileView`: the area sends the core's own
//! "unreadable" event for it (`ThreadEvent::AttachFailed`,
//! `DeskEvent::LogoUnreadable`).
//!
//! The type the core checks is sniffed from the file's first bytes
//! ([`file_kind`]), never taken from its name: a HEIC photo saved as `.jpg`
//! arrives as `image/heic`, and the core refuses it with its own words.

use district_core::{ATTACHMENT_TYPES, MAX_ATTACHMENT_BYTES, MAX_ATTACHMENTS, PickedAttachment};

/// The image types the service hosts as a help desk logo, as District AI for
/// Linux offers them (its `pages/desk_settings.rs`). The core does not check a
/// logo's type; the chooser offers only these.
pub(crate) const LOGO_TYPES: [&str; 3] = ["image/png", "image/jpeg", "image/webp"];

/// The name an upload carries when the file's own name is empty or nothing
/// but a path.
pub(crate) const UNNAMED: &str = "image";

/// What a file's first bytes say it is.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, uniffi::Enum)]
pub enum FileKind {
    /// PNG: `89 50 4E 47 0D 0A 1A 0A`.
    Png,
    /// JPEG: `FF D8 FF`.
    Jpeg,
    /// GIF: `GIF87a` or `GIF89a`.
    Gif,
    /// WebP: `RIFF`, four bytes of length, `WEBP`.
    Webp,
    /// HEIC: an ISO box `ftyp` whose major brand is one of HEVC-coded
    /// images' (`heic`, `heix`, `hevc`, `hevx`, `heim`, `heis`, `hevm`,
    /// `hevs`). The service takes none, and the core says so.
    Heic,
    /// HEIF of another coding: an `ftyp` box whose major brand is `mif1` or
    /// `msf1`.
    Heif,
    /// PDF: `%PDF-`.
    Pdf,
    /// Anything else, an empty or truncated file included.
    Unknown,
}

impl FileKind {
    /// The MIME type the core is handed for this kind. An unknown file is
    /// `application/octet-stream`, which no check accepts.
    pub fn mime_type(self) -> &'static str {
        match self {
            Self::Png => "image/png",
            Self::Jpeg => "image/jpeg",
            Self::Gif => "image/gif",
            Self::Webp => "image/webp",
            Self::Heic => "image/heic",
            Self::Heif => "image/heif",
            Self::Pdf => "application/pdf",
            Self::Unknown => "application/octet-stream",
        }
    }

    /// The extensions a file chooser filters on for this kind, each with its
    /// dot, or none for a kind the chooser never offers.
    fn extensions(self) -> &'static [&'static str] {
        match self {
            Self::Png => &[".png"],
            Self::Jpeg => &[".jpg", ".jpeg"],
            Self::Gif => &[".gif"],
            Self::Webp => &[".webp"],
            Self::Heic | Self::Heif | Self::Pdf | Self::Unknown => &[],
        }
    }

    /// The kind whose MIME type is `mime_type`, if any.
    fn of_mime_type(mime_type: &str) -> Self {
        KNOWN
            .into_iter()
            .find(|kind| kind.mime_type() == mime_type)
            .unwrap_or(Self::Unknown)
    }
}

/// Every kind but [`FileKind::Unknown`].
const KNOWN: [FileKind; 7] = [
    FileKind::Png,
    FileKind::Jpeg,
    FileKind::Gif,
    FileKind::Webp,
    FileKind::Heic,
    FileKind::Heif,
    FileKind::Pdf,
];

/// The major brands of an `ftyp` box that make a file HEIC.
const HEIC_BRANDS: [&[u8; 4]; 8] = [
    b"heic", b"heix", b"hevc", b"hevx", b"heim", b"heis", b"hevm", b"hevs",
];

/// The major brands of an `ftyp` box that make a file HEIF of another coding.
const HEIF_BRANDS: [&[u8; 4]; 2] = [b"mif1", b"msf1"];

/// What `head`, a file's first bytes, says the file is. Twelve bytes are
/// enough for every kind; fewer than a kind's signature is [`FileKind::Unknown`].
#[uniffi::export]
pub fn file_kind(head: Vec<u8>) -> FileKind {
    sniff(&head)
}

/// [`file_kind`], on a borrowed head.
fn sniff(head: &[u8]) -> FileKind {
    if head.starts_with(b"\x89PNG\r\n\x1a\n") {
        FileKind::Png
    } else if head.starts_with(&[0xff, 0xd8, 0xff]) {
        FileKind::Jpeg
    } else if head.starts_with(b"GIF87a") || head.starts_with(b"GIF89a") {
        FileKind::Gif
    } else if head.starts_with(b"%PDF-") {
        FileKind::Pdf
    } else if head.len() >= 12 && head.starts_with(b"RIFF") && &head[8..12] == b"WEBP" {
        FileKind::Webp
    } else if head.len() >= 12 && &head[4..8] == b"ftyp" {
        let brand = &head[8..12];
        if HEIC_BRANDS.iter().any(|known| known.as_slice() == brand) {
            FileKind::Heic
        } else if HEIF_BRANDS.iter().any(|known| known.as_slice() == brand) {
            FileKind::Heif
        } else {
            FileKind::Unknown
        }
    } else {
        FileKind::Unknown
    }
}

/// The MIME type the core is handed for `kind` (see [`FileKind::mime_type`]).
#[uniffi::export]
pub fn file_kind_mime_type(kind: FileKind) -> String {
    kind.mime_type().to_owned()
}

/// One file the member picked, as C# read it.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct PickedFileView {
    /// The file's name, without its folder.
    pub file_name: String,
    /// The file's size as Windows reported it, in bytes. It may be more than
    /// `bytes` holds: the read stops at [`FilePickView::read_cap`].
    pub size: u64,
    /// The file's bytes, from its start, no more than the pick's `read_cap`.
    pub bytes: Vec<u8>,
}

impl std::fmt::Debug for PickedFileView {
    /// Leaves the bytes out, as the core's own `PickedAttachment` does: they
    /// are customer data.
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("PickedFileView")
            .field("size", &self.size)
            .field("len", &self.bytes.len())
            .finish_non_exhaustive()
    }
}

/// What a file chooser offers and how much of each file C# reads.
#[derive(Clone, Debug, PartialEq, Eq, uniffi::Record)]
pub struct FilePickView {
    /// The extensions to filter on, each with its dot (`.png`), as
    /// `FileOpenPicker.FileTypeFilter` takes them.
    pub extensions: Vec<String>,
    /// How many files one pick may return.
    pub max_files: u32,
    /// How many bytes of each file to read at most: one past the largest the
    /// service takes, so the core sees a file is too large and says so.
    pub read_cap: u64,
}

/// The pick for `types`, each a MIME type, of at most `max_files` files.
fn pick(types: &[&str], max_files: usize) -> FilePickView {
    FilePickView {
        extensions: types
            .iter()
            .flat_map(|mime_type| FileKind::of_mime_type(mime_type).extensions())
            .map(|extension| (*extension).to_owned())
            .collect(),
        max_files: u32::try_from(max_files).unwrap_or(u32::MAX),
        read_cap: u64::try_from(MAX_ATTACHMENT_BYTES + 1).unwrap_or(u64::MAX),
    }
}

/// The pick for a reply's attachments: the core's image types, as many files
/// as one message may carry.
#[uniffi::export]
pub fn attachment_pick() -> FilePickView {
    pick(&ATTACHMENT_TYPES, MAX_ATTACHMENTS)
}

/// The pick for the help desk's logo: [`LOGO_TYPES`], one file.
#[uniffi::export]
pub fn logo_pick() -> FilePickView {
    pick(&LOGO_TYPES, 1)
}

/// `file` as the attachment the core checks and uploads: its type sniffed
/// from its bytes, its name without any folder (and [`UNNAMED`] when that
/// leaves nothing).
pub(crate) fn picked_attachment(file: PickedFileView) -> PickedAttachment {
    let name = file
        .file_name
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or_default()
        .chars()
        .filter(|c| !c.is_control())
        .collect::<String>();
    PickedAttachment {
        file_name: if name.trim().is_empty() {
            UNNAMED.to_owned()
        } else {
            name
        },
        mime_type: sniff(&file.bytes).mime_type().to_owned(),
        bytes: file.bytes,
    }
}

/// Why the core would refuse `file` as the next of `held` attachments, in its
/// own words, or `None` when it would take it.
#[uniffi::export]
pub fn attachment_problem(file: PickedFileView, held: u32) -> Option<String> {
    let held = usize::try_from(held).unwrap_or(usize::MAX);
    picked_attachment(file).problem(held)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Real prefixes of each kind, as the files begin.
    const PNG: &[u8] = b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR";
    const JPEG: &[u8] = &[
        0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, b'J', b'F', b'I', b'F', 0,
    ];
    const GIF87: &[u8] = b"GIF87a\x01\0\x01\0";
    const GIF89: &[u8] = b"GIF89a\x01\0\x01\0";
    const WEBP: &[u8] = b"RIFF\x24\0\0\0WEBPVP8 ";
    const HEIC: &[u8] = b"\0\0\0\x18ftypheic\0\0\0\0mif1heic";
    const HEIF: &[u8] = b"\0\0\0\x1cftypmif1\0\0\0\0mif1heif";
    const PDF: &[u8] = b"%PDF-1.7\n%\xe2\xe3\xcf\xd3";

    fn file(name: &str, bytes: &[u8]) -> PickedFileView {
        PickedFileView {
            file_name: name.to_owned(),
            size: bytes.len() as u64,
            bytes: bytes.to_vec(),
        }
    }

    #[test]
    fn each_kind_is_known_by_its_first_bytes() {
        let cases = [
            (PNG, FileKind::Png, "image/png"),
            (JPEG, FileKind::Jpeg, "image/jpeg"),
            (GIF87, FileKind::Gif, "image/gif"),
            (GIF89, FileKind::Gif, "image/gif"),
            (WEBP, FileKind::Webp, "image/webp"),
            (HEIC, FileKind::Heic, "image/heic"),
            (HEIF, FileKind::Heif, "image/heif"),
            (PDF, FileKind::Pdf, "application/pdf"),
        ];
        for (bytes, kind, mime_type) in cases {
            assert_eq!(sniff(bytes), kind, "{mime_type}");
            assert_eq!(file_kind_mime_type(kind), mime_type);
        }
        for brand in HEIC_BRANDS {
            let mut head = b"\0\0\0\x18ftyp".to_vec();
            head.extend_from_slice(brand);
            assert_eq!(file_kind(head), FileKind::Heic, "{brand:?}");
        }
        assert_eq!(sniff(b"\0\0\0\x18ftypmsf1"), FileKind::Heif);
    }

    #[test]
    fn hostile_or_short_heads_are_unknown() {
        let unknown: [&[u8]; 14] = [
            b"",
            b"\x89PNG\r\n\x1a",
            &[0xff, 0xd8],
            b"GIF8",
            b"GIF88a",
            b"%PDF",
            b"RIFF\x24\0\0\0WEB",
            b"RIFF\x24\0\0\0WAVEfmt ",
            b"\0\0\0\x18ftyphei",
            b"\0\0\0\x18ftypavif",
            b"\0\0\0\x18ftypisom",
            b"\0\0\0\x18moovheic",
            b"hello, world",
            &[0; 64],
        ];
        for head in unknown {
            assert_eq!(sniff(head), FileKind::Unknown, "{head:?}");
        }
        assert_eq!(
            file_kind_mime_type(FileKind::Unknown),
            "application/octet-stream"
        );
    }

    #[test]
    fn the_name_does_not_decide_the_type() {
        let png_as_jpg = picked_attachment(file("roof.jpg", PNG));
        assert_eq!(png_as_jpg.mime_type, "image/png");
        assert_eq!(png_as_jpg.file_name, "roof.jpg");
        let heic_as_jpg = picked_attachment(file("IMG_0001.jpg", HEIC));
        assert_eq!(heic_as_jpg.mime_type, "image/heic");
        let text_as_png = picked_attachment(file("notes.png", b"hello"));
        assert_eq!(text_as_png.mime_type, "application/octet-stream");
    }

    #[test]
    fn the_name_loses_its_folder_and_control_characters() {
        let named = |name: &str| picked_attachment(file(name, PNG)).file_name;
        assert_eq!(named("C:\\Users\\a\\roof.png"), "roof.png");
        assert_eq!(named("../../etc/roof.png"), "roof.png");
        assert_eq!(named("ro\r\nof\0.png"), "roof.png");
        assert_eq!(named(""), UNNAMED);
        assert_eq!(named("folder\\"), UNNAMED);
        assert_eq!(named(" \t"), UNNAMED);
    }

    #[test]
    fn the_core_decides_what_it_takes() {
        assert_eq!(attachment_problem(file("roof.png", PNG), 0), None);
        assert_eq!(attachment_problem(file("roof.webp", WEBP), 4), None);
        let unsupported = Some("Only JPEG, PNG, GIF or WebP images can be attached.".to_owned());
        assert_eq!(attachment_problem(file("IMG.heic", HEIC), 0), unsupported);
        assert_eq!(attachment_problem(file("doc.pdf", PDF), 0), unsupported);
        assert_eq!(attachment_problem(file("empty.png", b""), 0), unsupported);
        assert_eq!(
            attachment_problem(file("roof.png", PNG), 5),
            Some("You can attach up to 5 images to one message.".to_owned())
        );
        assert_eq!(
            attachment_problem(file("roof.png", PNG), u32::MAX),
            Some("You can attach up to 5 images to one message.".to_owned())
        );
        // Read to the cap: one byte too many.
        let mut large = PNG.to_vec();
        large.resize(MAX_ATTACHMENT_BYTES + 1, 0);
        assert_eq!(
            attachment_problem(file("large.png", &large), 0),
            Some("Attachments must be between 1 byte and 5 MB.".to_owned())
        );
        large.truncate(MAX_ATTACHMENT_BYTES);
        assert_eq!(attachment_problem(file("large.png", &large), 0), None);
    }

    #[test]
    fn the_picks_offer_what_the_core_and_the_service_take() {
        let attachments = attachment_pick();
        assert_eq!(
            attachments.extensions,
            [".jpg", ".jpeg", ".png", ".gif", ".webp"]
        );
        assert_eq!(attachments.max_files, 5);
        assert_eq!(attachments.read_cap, 5 * 1024 * 1024 + 1);
        let logo = logo_pick();
        assert_eq!(logo.extensions, [".png", ".jpg", ".jpeg", ".webp"]);
        assert_eq!(logo.max_files, 1);
        assert_eq!(logo.read_cap, attachments.read_cap);
        // Every type offered is one the sniffing names, so nothing offered
        // arrives as unknown.
        for mime_type in ATTACHMENT_TYPES.iter().chain(&LOGO_TYPES) {
            assert_ne!(FileKind::of_mime_type(mime_type), FileKind::Unknown);
        }
        assert_eq!(FileKind::of_mime_type("text/plain"), FileKind::Unknown);
        for kind in [
            FileKind::Heic,
            FileKind::Heif,
            FileKind::Pdf,
            FileKind::Unknown,
        ] {
            assert!(kind.extensions().is_empty(), "{kind:?}");
        }
    }

    #[test]
    fn debug_leaves_the_bytes_out() {
        let shown = format!("{:?}", file("roof.png", PNG));
        assert_eq!(shown, "PickedFileView { size: 16, len: 16, .. }");
    }
}
