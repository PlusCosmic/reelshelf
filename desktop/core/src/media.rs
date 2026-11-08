//! What the uploader reads from a clip before uploading it: its length from the MP4 or QuickTime header, and its MD5.

use std::fs::File;
use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::Path;

use md5::{Digest, Md5};

/// The length in seconds of an MP4 or QuickTime (MOV) file, from the `mvhd` box inside the top-level `moov` box, or
/// None when it has no readable one. Boxes before `moov` (often a huge `mdat`) are skipped by seeking, so only a few
/// kilobytes are read.
pub fn mp4_duration(path: &Path) -> Option<f64> {
    let mut file = File::open(path).ok()?;
    read_mp4_duration(&mut file)
}

pub fn read_mp4_duration<R: Read + Seek>(stream: &mut R) -> Option<f64> {
    let end = stream.seek(SeekFrom::End(0)).ok()?;
    let mut position = 0;
    while position + 8 <= end {
        let (kind, header, size) = read_box_header(stream, position, end)?;
        if &kind == b"moov" {
            return read_movie(stream, position + header, position + size);
        }
        position += size;
    }
    None
}

fn read_movie<R: Read + Seek>(stream: &mut R, start: u64, end: u64) -> Option<f64> {
    let mut position = start;
    while position + 8 <= end {
        let (kind, header, size) = read_box_header(stream, position, end)?;
        if &kind == b"mvhd" {
            return read_movie_header(stream, position + header, position + size);
        }
        position += size;
    }
    None
}

/// `mvhd`: a version byte and three flag bytes, then (version 0) 32-bit creation and modification times, timescale and
/// duration, or (version 1) 64-bit times, a 32-bit timescale and a 64-bit duration.
fn read_movie_header<R: Read + Seek>(stream: &mut R, start: u64, end: u64) -> Option<f64> {
    let mut header = [0u8; 32];
    let available = (end - start).min(header.len() as u64) as usize;
    if available < 20 {
        return None;
    }
    stream.seek(SeekFrom::Start(start)).ok()?;
    stream.read_exact(&mut header[..available]).ok()?;

    let u32_at = |at: usize| u32::from_be_bytes(header[at..at + 4].try_into().unwrap());
    let (timescale, duration, unknown) = if header[0] == 1 {
        if available < 32 {
            return None;
        }
        let duration = u64::from_be_bytes(header[24..32].try_into().unwrap());
        (u32_at(20), duration, duration == u64::MAX)
    } else {
        let duration = u32_at(16);
        (u32_at(12), u64::from(duration), duration == u32::MAX)
    };

    // An unknown duration is all ones; a recorder still writing may leave it at zero.
    if timescale == 0 || duration == 0 || unknown {
        return None;
    }
    Some(duration as f64 / f64::from(timescale))
}

/// A box's type, header length and whole length, or None when it runs past the end.
fn read_box_header<R: Read + Seek>(stream: &mut R, position: u64, end: u64) -> Option<([u8; 4], u64, u64)> {
    let mut header = [0u8; 16];
    stream.seek(SeekFrom::Start(position)).ok()?;
    stream.read_exact(&mut header[..8]).ok()?;
    let mut size = u64::from(u32::from_be_bytes(header[..4].try_into().unwrap()));
    let kind: [u8; 4] = header[4..8].try_into().unwrap();
    let mut header_size = 8;
    if size == 1 {
        // A 64-bit size follows the type.
        stream.read_exact(&mut header[8..16]).ok()?;
        size = u64::from_be_bytes(header[8..16].try_into().unwrap());
        header_size = 16;
    } else if size == 0 {
        // The box runs to the end of its container.
        size = end - position;
    }

    if size < header_size || position.checked_add(size)? > end {
        return None;
    }
    Some((kind, header_size, size))
}

/// A file's MD5, streamed, as lowercase hex: the form the website sends and the API compares.
pub fn md5_hex(path: &Path) -> io::Result<String> {
    let mut reader = BufReader::with_capacity(1024 * 1024, File::open(path)?);
    let mut hasher = Md5::new();
    let mut buffer = vec![0u8; 1024 * 1024];
    loop {
        let read = reader.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hasher.finalize().iter().map(|byte| format!("{byte:02x}")).collect())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    fn boxed(kind: &str, body: &[&[u8]]) -> Vec<u8> {
        let content: Vec<u8> = body.concat();
        [
            &((8 + content.len()) as u32).to_be_bytes()[..],
            kind.as_bytes(),
            &content,
        ]
        .concat()
    }

    fn movie_header(version: u8, timescale: u32, duration: u64) -> Vec<u8> {
        if version == 1 {
            boxed(
                "mvhd",
                &[
                    &[1, 0, 0, 0],
                    &0u64.to_be_bytes(),
                    &0u64.to_be_bytes(),
                    &timescale.to_be_bytes(),
                    &duration.to_be_bytes(),
                    &[0; 80],
                ],
            )
        } else {
            boxed(
                "mvhd",
                &[
                    &[0, 0, 0, 0],
                    &0u32.to_be_bytes(),
                    &0u32.to_be_bytes(),
                    &timescale.to_be_bytes(),
                    &(duration as u32).to_be_bytes(),
                    &[0; 80],
                ],
            )
        }
    }

    fn duration_of(bytes: Vec<u8>) -> Option<f64> {
        read_mp4_duration(&mut Cursor::new(bytes))
    }

    #[test]
    fn an_mp4_with_its_movie_box_first() {
        // NVIDIA's app writes ftyp, then moov (faststart), then mdat.
        let file = [
            boxed("ftyp", &[b"isom", &512u32.to_be_bytes(), b"isomiso2avc1mp41"]),
            boxed("moov", &[&movie_header(0, 1000, 41_250), &boxed("trak", &[&[0; 24]])]),
            boxed("mdat", &[&[0; 4096]]),
        ]
        .concat();

        assert_eq!(duration_of(file), Some(41.25));
    }

    #[test]
    fn a_quicktime_mov_with_a_wide_atom_and_the_movie_box_after_a_huge_mdat() {
        // OBS's .mov: ftyp "qt  ", a wide placeholder, an mdat with a 64-bit size, and moov at the end.
        let body = [0u8; 3000];
        let mdat = [
            &1u32.to_be_bytes()[..],
            b"mdat",
            &(16 + body.len() as u64).to_be_bytes(),
            &body,
        ]
        .concat();
        let file = [
            boxed("ftyp", &[b"qt  ", &0x2005_0300u32.to_be_bytes(), b"qt  "]),
            boxed("wide", &[]),
            mdat,
            boxed("moov", &[&boxed("udta", &[&[0; 12]]), &movie_header(0, 600, 80_400)]),
        ]
        .concat();

        assert_eq!(duration_of(file), Some(134.0));
    }

    #[test]
    fn a_version_one_movie_header_with_64_bit_times() {
        let file = [
            boxed("ftyp", &[b"mp42", &0u32.to_be_bytes()]),
            boxed("moov", &[&movie_header(1, 90_000, 5_400_000)]),
        ]
        .concat();

        assert_eq!(duration_of(file), Some(60.0));
    }

    #[test]
    fn reads_from_a_file_on_disk() {
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("clip.mp4");
        std::fs::write(
            &path,
            [boxed("ftyp", &[b"isom"]), boxed("moov", &[&movie_header(0, 30, 900)])].concat(),
        )
        .unwrap();

        assert_eq!(mp4_duration(&path), Some(30.0));
    }

    #[test]
    fn a_file_with_no_movie_box_or_a_recording_still_being_written_has_no_length() {
        assert_eq!(
            duration_of([boxed("ftyp", &[b"isom"]), boxed("mdat", &[&[0; 64]])].concat()),
            None
        );
        assert_eq!(
            duration_of([boxed("ftyp", &[b"isom"]), boxed("moov", &[&movie_header(0, 1000, 0)])].concat()),
            None
        );
        assert_eq!(duration_of(b"not a video at all".to_vec()), None);
        // A box claiming more bytes than the file has.
        assert_eq!(
            duration_of([&9999u32.to_be_bytes()[..], b"moov", &[0; 8]].concat()),
            None
        );
    }

    #[test]
    fn md5_is_lowercase_hex() {
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("clip.mp4");
        std::fs::write(&path, b"The quick brown fox jumps over the lazy dog").unwrap();

        assert_eq!(md5_hex(&path).unwrap(), "9e107d9d372bb6826bd81d3542a419d6");
    }
}
