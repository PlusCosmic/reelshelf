//! Fakes for the core's tests: a clock tests move by hand, the platform parts, and the Reelshelf API with R2 behind
//! the [`UploaderApi`] trait. Ported from the .NET core's `Fakes.cs`.
#![allow(dead_code)]

use std::collections::{BTreeMap, HashMap, VecDeque};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

use chrono::{DateTime, FixedOffset, TimeDelta, TimeZone, Utc};
use futures_util::StreamExt;
use reelshelf_core::BoxFuture;
use reelshelf_core::api::{
    ApiError, ApiResult, DeviceAuthorization, MultipartTarget, PartBody, PrepareClip, PreparedClip, PresignedPart,
    PresignedParts, Problem, UploadTarget, UploadedPart, UploaderApi,
};
use reelshelf_core::engine::{EngineParts, UploaderEngine};
use reelshelf_core::ledger::{Ledger, LedgerEntry, LedgerState};
use reelshelf_core::matching::Book;
use reelshelf_core::monitor::root_key;
use reelshelf_core::notifications::Notification;
use reelshelf_core::platform::{Clock, CredentialStore, GameActivity, Notifier};
use reelshelf_core::settings::{SettingsStore, UploaderSettings};
use reelshelf_core::uploads::{MultipartUploader, OpenGate, PlanLimits, TokenBucket, UploadGate, UploadPipeline};
use tokio::sync::Notify;
use uuid::Uuid;

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

type DelayHook = Box<dyn Fn(Duration) + Send + Sync>;

/// A clock tests move by hand. Waiting moves it on at once, so loops run without real time passing. It keeps UTC.
pub struct FakeClock {
    now: Mutex<DateTime<Utc>>,
    delays: Mutex<Vec<Duration>>,
    on_delay: Mutex<Option<DelayHook>>,
}

impl FakeClock {
    pub fn new() -> Self {
        FakeClock {
            now: Mutex::new(Utc.with_ymd_and_hms(2026, 11, 6, 20, 47, 0).unwrap()),
            delays: Mutex::new(Vec::new()),
            on_delay: Mutex::new(None),
        }
    }

    pub fn advance(&self, by: Duration) {
        *lock(&self.now) += TimeDelta::from_std(by).unwrap();
    }

    pub fn set(&self, at: DateTime<Utc>) {
        *lock(&self.now) = at;
    }

    pub fn delays(&self) -> Vec<Duration> {
        lock(&self.delays).clone()
    }

    pub fn clear_delays(&self) {
        lock(&self.delays).clear();
    }

    /// Called after each wait, so a test can change the world while the code under test waits.
    pub fn on_delay(&self, hook: impl Fn(Duration) + Send + Sync + 'static) {
        *lock(&self.on_delay) = Some(Box::new(hook));
    }
}

impl Clock for FakeClock {
    fn now(&self) -> DateTime<Utc> {
        *lock(&self.now)
    }

    fn local_offset(&self, _at: DateTime<Utc>) -> FixedOffset {
        FixedOffset::east_opt(0).unwrap()
    }

    fn sleep(&self, duration: Duration) -> BoxFuture<'static, ()> {
        lock(&self.delays).push(duration);
        self.advance(duration);
        if let Some(hook) = lock(&self.on_delay).as_ref() {
            hook(duration);
        }
        Box::pin(async {})
    }
}

pub struct MemoryCredentials {
    token: Mutex<Option<String>>,
}

impl MemoryCredentials {
    pub fn new(token: Option<&str>) -> Self {
        MemoryCredentials {
            token: Mutex::new(token.map(str::to_string)),
        }
    }

    pub fn token(&self) -> Option<String> {
        lock(&self.token).clone()
    }
}

impl CredentialStore for MemoryCredentials {
    fn read_token(&self) -> Option<String> {
        self.token()
    }

    fn write_token(&self, token: &str) {
        *lock(&self.token) = Some(token.to_string());
    }

    fn delete_token(&self) {
        *lock(&self.token) = None;
    }
}

#[derive(Default)]
pub struct FakeGame {
    running: AtomicBool,
}

impl FakeGame {
    pub fn set(&self, running: bool) {
        self.running.store(running, Ordering::SeqCst);
    }
}

impl GameActivity for FakeGame {
    fn is_game_running(&self) -> bool {
        self.running.load(Ordering::SeqCst)
    }
}

/// Records each notification as `N1 Apex Legends:2, Valorant:1`, `N2 folder:2`, `N3 100:false`, `N4` or `N5`.
#[derive(Default)]
pub struct RecordingNotifier {
    sent: Mutex<Vec<String>>,
}

impl RecordingNotifier {
    pub fn sent(&self) -> Vec<String> {
        lock(&self.sent).clone()
    }
}

impl Notifier for RecordingNotifier {
    fn post(&self, notification: Notification) {
        let line = match &notification {
            Notification::ClipsShelved { books } => format!(
                "N1 {}",
                books
                    .iter()
                    .map(|book| format!("{}:{}", book.book_name, book.clips))
                    .collect::<Vec<_>>()
                    .join(", ")
            ),
            Notification::NeedsBook { folder, waiting } => format!("N2 {folder}:{waiting}"),
            Notification::LibraryFull(full) => format!(
                "N3 {}:{}",
                full.clip_limit.map(|limit| limit.to_string()).unwrap_or_default(),
                full.monthly_limit
            ),
            Notification::UpdateRequired => "N4".to_string(),
            Notification::SignedOut => "N5".to_string(),
        };
        lock(&self.sent).push(line);
    }
}

/// A clip prepared on the fake API, with its open upload.
#[derive(Debug, Clone)]
pub struct FakeClip {
    pub id: Uuid,
    pub upload_id: String,
    pub size: u64,
    pub part_count: u32,
    /// ETag and size of each part storage holds.
    pub parts: BTreeMap<u32, (String, u64)>,
    pub completed: bool,
    pub deleted: bool,
    pub upload_gone: bool,
    pub category_id: Uuid,
    pub title: String,
    pub md5: String,
    pub created_at: DateTime<Utc>,
}

pub fn problem(status: u16, title: Option<&str>, detail: Option<&str>) -> ApiError {
    match status {
        401 => ApiError::SignedOut,
        426 => ApiError::UpdateRequired,
        _ => ApiError::Status(Problem {
            status,
            title: title.map(str::to_string),
            detail: detail.map(str::to_string),
            error: None,
        }),
    }
}

/// An RFC 8628 refusal: 400 with `{ error }`.
pub fn oauth_error(error: &str) -> ApiError {
    ApiError::Status(Problem {
        status: 400,
        error: Some(error.to_string()),
        ..Problem::default()
    })
}

pub const STORAGE_HOST: &str = "https://r2.reelshelf.test";

pub struct ServerState {
    pub books: Vec<Book>,
    pub max_clip_seconds: Option<i64>,
    pub clip_limit: Option<i64>,
    pub clip_count: i64,
    pub new_clip_limit: Option<i64>,
    pub new_clips: i64,
    /// The part size the API gives each upload.
    pub part_size: u64,
    pub protocol: String,
    pub clips: HashMap<Uuid, FakeClip>,
    /// Clip ids in the order they were prepared.
    pub clip_order: Vec<Uuid>,
    pub requests: Vec<String>,
    /// Answers for preparing, in turn; once used up, clips are prepared normally.
    pub prepare_answers: VecDeque<ApiError>,
    /// Answers for completing, in turn; once used up, completing succeeds.
    pub complete_answers: VecDeque<ApiError>,
    /// Answers for part PUTs, in turn (None is a network error); once used up, parts go in.
    pub put_answers: VecDeque<Option<u16>>,
    /// Every API call fails with a network error while set.
    pub api_down: bool,
    /// Every API call answers this while set.
    pub refuse_with: Option<ApiError>,
    pub puts: usize,
    /// Called on every part PUT that goes in, with its number.
    pub on_part: Option<Arc<dyn Fn(u32) + Send + Sync>>,
    /// After this part goes in, the PUT never answers: the app stopped mid-request.
    pub hang_after_part: Option<u32>,
    pub authorize_answers: VecDeque<ApiResult<DeviceAuthorization>>,
    pub token_answers: VecDeque<ApiResult<String>>,
    pub device_names: Vec<String>,
    pub polled_codes: Vec<String>,
}

/// The Reelshelf API and R2 in one: books, the plan, preparing clips, part URLs, part PUTs, the part list and
/// completion, with hooks for the answers tests need (409, 403, 422, 503, other protocols, failing parts).
pub struct FakeReelshelf {
    clock: Arc<FakeClock>,
    state: Mutex<ServerState>,
    /// Notified when a PUT hangs after [`ServerState::hang_after_part`].
    pub hung: Notify,
}

impl FakeReelshelf {
    pub fn new(clock: Arc<FakeClock>) -> Self {
        FakeReelshelf {
            clock,
            state: Mutex::new(ServerState {
                books: Vec::new(),
                max_clip_seconds: Some(60),
                clip_limit: Some(100),
                clip_count: 0,
                new_clip_limit: Some(200),
                new_clips: 0,
                part_size: 1024,
                protocol: "s3-multipart".to_string(),
                clips: HashMap::new(),
                clip_order: Vec::new(),
                requests: Vec::new(),
                prepare_answers: VecDeque::new(),
                complete_answers: VecDeque::new(),
                put_answers: VecDeque::new(),
                api_down: false,
                refuse_with: None,
                puts: 0,
                on_part: None,
                hang_after_part: None,
                authorize_answers: VecDeque::new(),
                token_answers: VecDeque::new(),
                device_names: Vec::new(),
                polled_codes: Vec::new(),
            }),
            hung: Notify::new(),
        }
    }

    pub fn state(&self) -> MutexGuard<'_, ServerState> {
        lock(&self.state)
    }

    /// The clips in the order they were prepared.
    pub fn clips(&self) -> Vec<FakeClip> {
        let state = self.state();
        state.clip_order.iter().map(|id| state.clips[id].clone()).collect()
    }

    pub fn clip(&self, id: Uuid) -> FakeClip {
        self.state().clips[&id].clone()
    }

    pub fn puts(&self) -> usize {
        self.state().puts
    }

    pub fn requests(&self) -> Vec<String> {
        self.state().requests.clone()
    }

    fn call(&self, request: String) -> ApiResult<MutexGuard<'_, ServerState>> {
        let mut state = self.state();
        state.requests.push(request);
        if state.api_down {
            return Err(ApiError::Network("The network is down".to_string()));
        }
        if let Some(error) = state.refuse_with.clone() {
            return Err(error);
        }
        Ok(state)
    }

    fn not_open() -> ApiError {
        problem(404, Some("Resource Not Found"), Some("This upload is no longer open"))
    }
}

impl UploaderApi for FakeReelshelf {
    async fn authorize(&self, device_name: &str) -> ApiResult<DeviceAuthorization> {
        let mut state = self.call("POST /api/devices/authorize".to_string())?;
        state.device_names.push(device_name.to_string());
        state
            .authorize_answers
            .pop_front()
            .unwrap_or_else(|| Err(ApiError::Network("no answer".to_string())))
    }

    async fn poll_token(&self, device_code: &str) -> ApiResult<String> {
        let mut state = self.call("POST /api/devices/token".to_string())?;
        state.polled_codes.push(device_code.to_string());
        state
            .token_answers
            .pop_front()
            .unwrap_or_else(|| Err(ApiError::Network("no answer".to_string())))
    }

    async fn sign_out(&self) -> ApiResult<()> {
        drop(self.call("DELETE /api/devices/current".to_string())?);
        Ok(())
    }

    async fn books(&self) -> ApiResult<Vec<Book>> {
        Ok(self.call("GET /api/games/categories".to_string())?.books.clone())
    }

    async fn plan(&self) -> ApiResult<PlanLimits> {
        let state = self.call("GET /api/me/plan".to_string())?;
        Ok(PlanLimits {
            max_clip_seconds: state.max_clip_seconds,
            clip_limit: state.clip_limit,
            new_clip_limit: state.new_clip_limit,
            clip_count: state.clip_count,
            new_clips: state.new_clips,
        })
    }

    async fn prepare(&self, clip: &PrepareClip) -> ApiResult<PreparedClip> {
        let mut state = self.call(format!("POST /api/clips/categories/{}/videos", clip.category_id))?;
        if let Some(answer) = state.prepare_answers.pop_front() {
            return Err(answer);
        }
        if !state.books.iter().any(|book| book.id == clip.category_id) {
            return Err(problem(404, Some("Resource Not Found"), Some("No such book")));
        }

        let id = Uuid::new_v4();
        let part_size = state.part_size;
        let part_count = clip.file_size.div_ceil(part_size).max(1) as u32;
        let upload_id = format!("upload-{}", state.clips.len() + 1);
        state.clips.insert(
            id,
            FakeClip {
                id,
                upload_id: upload_id.clone(),
                size: clip.file_size,
                part_count,
                parts: BTreeMap::new(),
                completed: false,
                deleted: false,
                upload_gone: false,
                category_id: clip.category_id,
                title: clip.title.clone(),
                md5: clip.md5.clone(),
                created_at: clip.created_at,
            },
        );
        state.clip_order.push(id);
        Ok(PreparedClip {
            clip_id: id,
            upload: UploadTarget {
                protocol: state.protocol.clone(),
                multipart: Some(MultipartTarget {
                    upload_id,
                    part_size,
                    part_count,
                }),
            },
        })
    }

    async fn delete_clip(&self, clip_id: Uuid) -> ApiResult<()> {
        let mut state = self.call(format!("DELETE /api/clips/videos/{clip_id}"))?;
        match state.clips.get_mut(&clip_id) {
            Some(clip) => {
                clip.deleted = true;
                Ok(())
            }
            None => Err(problem(404, None, None)),
        }
    }

    async fn presign_parts(&self, clip_id: Uuid, _upload_id: &str, part_numbers: &[u32]) -> ApiResult<PresignedParts> {
        let state = self.call(format!("POST /api/clips/videos/{clip_id}/upload/parts"))?;
        match state.clips.get(&clip_id) {
            Some(clip) if !clip.deleted && !clip.upload_gone => Ok(PresignedParts {
                expires_at: Some(self.clock.now() + TimeDelta::days(5 * 365)),
                parts: part_numbers
                    .iter()
                    .map(|&number| PresignedPart {
                        part_number: number,
                        url: format!("{STORAGE_HOST}/{clip_id}/{number}"),
                    })
                    .collect(),
            }),
            _ => Err(Self::not_open()),
        }
    }

    async fn list_parts(&self, clip_id: Uuid, _upload_id: &str) -> ApiResult<Vec<UploadedPart>> {
        let state = self.call(format!("GET /api/clips/videos/{clip_id}/upload/parts"))?;
        match state.clips.get(&clip_id) {
            Some(clip) if !clip.deleted && !clip.upload_gone => Ok(clip
                .parts
                .iter()
                .map(|(&part_number, (etag, size))| UploadedPart {
                    part_number,
                    etag: etag.clone(),
                    size: *size,
                })
                .collect()),
            _ => Err(Self::not_open()),
        }
    }

    async fn complete(&self, clip_id: Uuid, _upload_id: &str, parts: &BTreeMap<u32, String>) -> ApiResult<()> {
        let mut state = self.call(format!("POST /api/clips/videos/{clip_id}/upload/complete"))?;
        if let Some(answer) = state.complete_answers.pop_front() {
            return Err(answer);
        }
        let Some(clip) = state
            .clips
            .get_mut(&clip_id)
            .filter(|clip| !clip.deleted && !clip.upload_gone)
        else {
            return Err(Self::not_open());
        };
        let every_part = parts.len() == clip.part_count as usize
            && parts
                .iter()
                .all(|(number, etag)| clip.parts.get(number).is_some_and(|(held, _)| held == etag));
        if !every_part {
            return Err(problem(
                400,
                Some("Bad Request"),
                Some("The parts don't match the upload"),
            ));
        }
        clip.completed = true;
        Ok(())
    }

    async fn put_part(&self, url: &str, length: u64, mut body: PartBody) -> ApiResult<Option<String>> {
        let path = url
            .strip_prefix(STORAGE_HOST)
            .expect("parts go to storage")
            .trim_start_matches('/');
        let (clip_id, number) = path.split_once('/').expect("a part URL names its clip and part");
        let clip_id = Uuid::parse_str(clip_id).unwrap();
        let number: u32 = number.parse().unwrap();
        {
            let mut state = self.state();
            state.puts += 1;
            state.requests.push(format!("PUT {url}"));
        }

        let mut bytes = Vec::new();
        while let Some(chunk) = body.next().await {
            bytes.extend_from_slice(&chunk.expect("the part reads"));
        }
        assert_eq!(bytes.len() as u64, length, "a part's body is its declared length");

        let (on_part, hang) = {
            let mut state = self.state();
            if let Some(answer) = state.put_answers.pop_front() {
                return match answer {
                    None => Err(ApiError::Network("The connection was reset".to_string())),
                    Some(status) => Err(problem(status, None, None)),
                };
            }
            let etag = format!("\"etag-{number}-{}\"", bytes.len());
            state
                .clips
                .get_mut(&clip_id)
                .expect("the clip was prepared")
                .parts
                .insert(number, (etag, bytes.len() as u64));
            (state.on_part.clone(), state.hang_after_part == Some(number))
        };
        if let Some(on_part) = on_part {
            on_part(number);
        }
        if hang {
            self.hung.notify_one();
            std::future::pending::<()>().await;
        }
        Ok(Some(format!("\"etag-{number}-{}\"", bytes.len())))
    }
}

/// A recordings folder, a ledger and settings in a temporary directory, with the fake API and storage.
pub struct TestUploader {
    pub home: tempfile::TempDir,
    pub clock: Arc<FakeClock>,
    pub server: Arc<FakeReelshelf>,
    pub credentials: Arc<MemoryCredentials>,
    pub game: Arc<FakeGame>,
    pub notifier: Arc<RecordingNotifier>,
    pub ledger: Arc<Ledger>,
}

/// The plan the pipeline tests upload under: clips up to 60 s, 100 clips, 200 a month.
pub const PLAN: PlanLimits = PlanLimits {
    max_clip_seconds: Some(60),
    clip_limit: Some(100),
    new_clip_limit: Some(200),
    clip_count: 0,
    new_clips: 0,
};

impl TestUploader {
    pub fn new() -> Self {
        let home = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(home.path().join("Videos").join("NVIDIA")).unwrap();
        let clock = Arc::new(FakeClock::new());
        let ledger = Arc::new(Ledger::open(&home.path().join("ledger.db")).unwrap());
        TestUploader {
            server: Arc::new(FakeReelshelf::new(clock.clone())),
            clock,
            credentials: Arc::new(MemoryCredentials::new(Some("rsd_test"))),
            game: Arc::new(FakeGame::default()),
            notifier: Arc::new(RecordingNotifier::default()),
            ledger,
            home,
        }
    }

    pub fn recordings(&self) -> PathBuf {
        self.home.path().join("Videos").join("NVIDIA")
    }

    /// The recordings folder as the ledger keys it.
    pub fn root(&self) -> String {
        root_key(self.recordings().to_str().unwrap())
    }

    pub fn settings_store(&self) -> SettingsStore {
        SettingsStore::new(self.home.path().join("settings.json"))
    }

    pub fn add_book(&self, name: &str) -> Book {
        let book = Book::new(Uuid::new_v4(), name, Some(&name.to_lowercase().replace(' ', "-")));
        self.server.state().books.push(book.clone());
        book
    }

    /// Writes a file below the recordings folder and gives it a last-write time a minute in the past (or `last_write`).
    pub fn write_file(&self, relative_path: &str, size: usize, last_write: Option<DateTime<Utc>>) -> PathBuf {
        let path = self.full(relative_path);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, noise(relative_path, size)).unwrap();
        set_modified(
            &path,
            last_write.unwrap_or_else(|| self.clock.now() - TimeDelta::minutes(1)),
        );
        path
    }

    pub fn full(&self, relative_path: &str) -> PathBuf {
        relative_path
            .split('/')
            .fold(self.recordings(), |path, segment| path.join(segment))
    }

    pub fn full_path(&self, entry: &LedgerEntry) -> PathBuf {
        self.full(&entry.path)
    }

    pub fn engine(&self, configure: impl FnOnce(&mut UploaderSettings)) -> Arc<UploaderEngine<FakeReelshelf>> {
        let store = self.settings_store();
        let mut settings = store.load();
        configure(&mut settings);
        store.save(&settings).unwrap();
        UploaderEngine::new(EngineParts {
            api: self.server.clone(),
            ledger: self.ledger.clone(),
            settings: store,
            credentials: self.credentials.clone(),
            game: self.game.clone(),
            notifier: self.notifier.clone(),
            clock: self.clock.clone(),
            can_open: Arc::new(|_: &Path| true),
        })
    }

    pub fn pipeline(&self, gate: Option<Arc<dyn UploadGate>>) -> UploadPipeline<FakeReelshelf> {
        let clock: Arc<dyn Clock> = self.clock.clone();
        UploadPipeline::new(
            self.server.clone(),
            self.ledger.clone(),
            MultipartUploader::new(
                self.server.clone(),
                self.ledger.clone(),
                clock.clone(),
                Arc::new(TokenBucket::new(clock.clone())),
                gate.unwrap_or_else(|| Arc::new(OpenGate)),
                None,
            ),
            clock,
        )
    }

    /// A settled file as the monitor adds it: waiting on a book.
    pub fn waiting(&self, relative_path: &str, book_id: Uuid, size: usize) -> LedgerEntry {
        let path = self.write_file(relative_path, size, None);
        let metadata = std::fs::metadata(&path).unwrap();
        let file = reelshelf_core::files::ScannedFile {
            relative_path: relative_path.to_string(),
            size: metadata.len(),
            last_write: DateTime::<Utc>::from(metadata.modified().unwrap()),
        };
        let mut entry = LedgerEntry::new(&self.root(), &file, LedgerState::Waiting, self.clock.now());
        entry.category_id = Some(book_id);
        self.ledger.save(&entry);
        entry
    }
}

pub fn set_modified(path: &Path, at: DateTime<Utc>) {
    let file = std::fs::OpenOptions::new().write(true).open(path).unwrap();
    file.set_modified(at.into()).unwrap();
}

/// Bytes that differ by path, so files hash apart.
pub fn noise(seed: &str, size: usize) -> Vec<u8> {
    let mut state: u64 = seed.bytes().fold(0xcbf2_9ce4_8422_2325, |hash, byte| {
        (hash ^ u64::from(byte)).wrapping_mul(0x0100_0000_01b3)
    });
    (0..size)
        .map(|_| {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            state as u8
        })
        .collect()
}

/// A small MP4 whose movie header gives this length.
pub fn mp4(seconds: f64) -> Vec<u8> {
    let mut mvhd = vec![0u8; 8 + 100];
    let length = mvhd.len() as u32;
    mvhd[..4].copy_from_slice(&length.to_be_bytes());
    mvhd[4..8].copy_from_slice(b"mvhd");
    mvhd[8 + 12..8 + 16].copy_from_slice(&1000u32.to_be_bytes());
    mvhd[8 + 16..8 + 20].copy_from_slice(&((seconds * 1000.0) as u32).to_be_bytes());
    let mut moov = ((8 + mvhd.len()) as u32).to_be_bytes().to_vec();
    moov.extend_from_slice(b"moov");
    moov.extend_from_slice(&mvhd);
    let mut mdat = ((8 + 2000) as u32).to_be_bytes().to_vec();
    mdat.extend_from_slice(b"mdat");
    mdat.extend_from_slice(&noise("mdat", 2000));
    [moov, mdat].concat()
}
