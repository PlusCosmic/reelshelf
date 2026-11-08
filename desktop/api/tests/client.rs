//! The client against a local mock server: the headers it sends, how each status maps, and the token poll's codes.

use chrono::{TimeZone, Utc};
use reelshelf_api::*;
use reqwest::StatusCode;
use serde_json::json;
use uuid::Uuid;
use wiremock::matchers::{body_json, header, method, path, query_param};
use wiremock::{Mock, MockServer, Request, ResponseTemplate};

const VERSION: &str = "1.2.3";
const TOKEN: &str = "rsd_test-token";

fn client(server: &MockServer) -> ApiClient {
    ApiClient::new(&server.uri(), VERSION).unwrap()
}

fn signed_in(server: &MockServer) -> ApiClient {
    let client = client(server);
    client.set_token(Some(TOKEN.to_owned()));
    client
}

fn plan() -> serde_json::Value {
    json!({
        "tier": "free", "clip_count": 3, "clip_limit": 100, "new_clips_last30_days": 3, "new_clip_limit": 200,
        "max_clip_seconds": 60, "storage_used_bytes": 1024, "has_transcripts": false, "billing_enabled": true,
        "can_manage_billing": false, "subscription": null, "clips_over_limit": 0, "over_limit_deletes_at": null,
        "free_clip_limit": 100, "over_limit_grace_days": 14, "can_change_plan": false, "granted_tier": null
    })
}

/// A plan the server answers with `status` and `body`, read signed in.
async fn plan_answering(status: u16, body: serde_json::Value) -> ApiError {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/api/me/plan"))
        .respond_with(ResponseTemplate::new(status).set_body_json(body))
        .mount(&server)
        .await;
    signed_in(&server).plan().await.unwrap_err()
}

fn problem(title: &str, detail: &str) -> serde_json::Value {
    json!({ "type": null, "title": title, "status": 0, "detail": detail, "instance": "/api" })
}

#[tokio::test]
async fn api_requests_carry_the_token_and_the_client_header() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/api/me/plan"))
        .and(header("authorization", format!("Bearer {TOKEN}").as_str()))
        .and(header(CLIENT_HEADER, "uploader/1.2.3"))
        .respond_with(ResponseTemplate::new(200).set_body_json(plan()))
        .expect(1)
        .mount(&server)
        .await;

    let plan = signed_in(&server).plan().await.unwrap();
    assert_eq!(plan.max_clip_seconds, Some(60));
    assert_eq!(plan.subscription, None);
}

#[tokio::test]
async fn without_a_token_no_authorization_header_is_sent() {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/api/devices/authorize"))
        .and(header(CLIENT_HEADER, "uploader/1.2.3"))
        .and(body_json(json!({ "device_name": "Desk PC" })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "device_code": "dc", "user_code": "BCDFGHJK", "verification_uri": "https://reelshelf.app/device",
            "verification_uri_complete": "https://reelshelf.app/device?code=BCDF-GHJK", "interval": 5, "expires_in": 600
        })))
        .mount(&server)
        .await;

    let started = client(&server).authorize_device("Desk PC").await.unwrap();
    assert_eq!(started.user_code, "BCDFGHJK");
    assert_eq!(started.interval, 5);

    let requests = server.received_requests().await.unwrap();
    assert!(requests[0].headers.get("authorization").is_none());
}

#[tokio::test]
async fn a_cleared_token_stops_being_sent() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .and(path("/api/games/categories"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!([])))
        .mount(&server)
        .await;
    let client = signed_in(&server);
    client.set_token(None);

    assert!(client.game_categories().await.unwrap().is_empty());
    let requests = server.received_requests().await.unwrap();
    assert!(requests[0].headers.get("authorization").is_none());
}

#[tokio::test]
async fn reads_the_books() {
    let server = MockServer::start().await;
    let id = Uuid::new_v4();
    Mock::given(method("GET"))
        .and(path("/api/games/categories"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!([{
            "id": id, "name": "Apex Legends", "slug": "apex-legends", "cover_url": null, "key_art_url": null,
            "game_logo_url": null, "is_custom": false, "cloth_color": "#336699", "extra": "ignored"
        }])))
        .mount(&server)
        .await;

    let books = signed_in(&server).game_categories().await.unwrap();
    assert_eq!(books.len(), 1);
    assert_eq!(books[0].id, id);
    assert_eq!(books[0].cloth_color.as_deref(), Some("#336699"));
}

#[tokio::test]
async fn create_video_sends_the_document_query() {
    let server = MockServer::start().await;
    let category = Uuid::new_v4();
    let clip = Uuid::new_v4();
    Mock::given(method("POST"))
        .and(path(format!("/api/clips/categories/{category}/videos")))
        .and(query_param("videoTitle", "Clutch win"))
        .and(query_param("fileSize", "12345"))
        .and(query_param("createdAt", "2026-11-08T12:30:00Z"))
        .and(query_param("md5Hash", "abc123"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "clip_id": clip, "video_id": clip,
            "upload": {
                "protocol": "s3-multipart", "endpoint": format!("/api/clips/videos/{clip}/upload"),
                "headers": {}, "metadata": {}, "expires_at": "2026-11-09T12:30:00Z",
                "multipart": { "upload_id": "up-1", "part_size": 8388608, "part_count": 2 }
            }
        })))
        .expect(1)
        .mount(&server)
        .await;

    let created = signed_in(&server)
        .create_video(
            category,
            &CreateVideoQuery {
                video_title: "Clutch win".into(),
                file_size: 12345,
                created_at: Some(Utc.with_ymd_and_hms(2026, 11, 8, 12, 30, 0).unwrap()),
                md5_hash: Some("abc123".into()),
            },
        )
        .await
        .unwrap();
    assert_eq!(created.clip_id, clip);
    assert_eq!(created.upload.protocol, UploadTarget::S3_MULTIPART);
    assert_eq!(created.upload.multipart.unwrap().part_count, 2);
}

#[tokio::test]
async fn create_video_leaves_out_what_it_hasnt() {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .respond_with(ResponseTemplate::new(409).set_body_json("A video with this MD5 hash already exists"))
        .mount(&server)
        .await;

    let error = signed_in(&server)
        .create_video(
            Uuid::new_v4(),
            &CreateVideoQuery {
                video_title: "Clip".into(),
                file_size: 1,
                created_at: None,
                md5_hash: None,
            },
        )
        .await
        .unwrap_err();
    let ApiError::Conflict(problem) = error else {
        panic!("{error:?}")
    };
    assert_eq!(problem.message(), Some("A video with this MD5 hash already exists"));

    let request = &server.received_requests().await.unwrap()[0];
    assert_eq!(request.url.query(), Some("videoTitle=Clip&fileSize=1"));
}

#[tokio::test]
async fn multipart_calls_send_their_bodies() {
    let server = MockServer::start().await;
    let clip = Uuid::new_v4();
    let base = format!("/api/clips/videos/{clip}/upload");
    Mock::given(method("POST"))
        .and(path(format!("{base}/parts")))
        .and(body_json(json!({ "upload_id": "up-1", "part_numbers": [1, 2] })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "parts": [{ "part_number": 1, "url": "https://r2/1" }, { "part_number": 2, "url": "https://r2/2" }],
            "expires_at": "2026-11-08T13:00:00Z"
        })))
        .mount(&server)
        .await;
    Mock::given(method("GET"))
        .and(path(format!("{base}/parts")))
        .and(query_param("uploadId", "up-1"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "parts": [{ "part_number": 1, "etag": "\"e1\"", "size": 8388608 }]
        })))
        .mount(&server)
        .await;
    Mock::given(method("POST"))
        .and(path(format!("{base}/complete")))
        .and(body_json(json!({
            "upload_id": "up-1",
            "parts": [{ "part_number": 1, "etag": "\"e1\"" }, { "part_number": 2, "etag": "\"e2\"" }]
        })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({ "clip_id": clip, "status": "uploaded" })))
        .mount(&server)
        .await;
    let client = signed_in(&server);

    let urls = client
        .presign_parts(
            clip,
            &PresignUploadPartsRequest {
                upload_id: "up-1".into(),
                part_numbers: vec![1, 2],
            },
        )
        .await
        .unwrap();
    assert_eq!(urls.parts[1].url, "https://r2/2");

    let held = client.list_parts(clip, "up-1").await.unwrap();
    assert_eq!(held.parts[0].etag, "\"e1\"");

    let completed = client
        .complete_upload(
            clip,
            &CompleteUploadRequest {
                upload_id: "up-1".into(),
                parts: vec![
                    CompletedUploadPart {
                        part_number: 1,
                        etag: "\"e1\"".into(),
                    },
                    CompletedUploadPart {
                        part_number: 2,
                        etag: "\"e2\"".into(),
                    },
                ],
            },
        )
        .await
        .unwrap();
    assert_eq!(completed.status, ClipVideoStatus::Uploaded);
}

#[tokio::test]
async fn an_unknown_status_reads_as_unknown() {
    let server = MockServer::start().await;
    let clip = Uuid::new_v4();
    Mock::given(method("POST"))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({ "clip_id": clip, "status": "queued" })))
        .mount(&server)
        .await;

    let completed = signed_in(&server)
        .complete_upload(
            clip,
            &CompleteUploadRequest {
                upload_id: "u".into(),
                parts: vec![],
            },
        )
        .await
        .unwrap();
    assert_eq!(completed.status, ClipVideoStatus::Unknown);
}

#[tokio::test]
async fn delete_clip_and_sign_out_need_no_body() {
    let server = MockServer::start().await;
    let clip = Uuid::new_v4();
    Mock::given(method("DELETE"))
        .and(path(format!("/api/clips/videos/{clip}")))
        .respond_with(ResponseTemplate::new(200))
        .expect(1)
        .mount(&server)
        .await;
    Mock::given(method("DELETE"))
        .and(path("/api/devices/current"))
        .and(header("authorization", format!("Bearer {TOKEN}").as_str()))
        .respond_with(ResponseTemplate::new(204))
        .expect(1)
        .mount(&server)
        .await;
    let client = signed_in(&server);

    client.delete_clip(clip).await.unwrap();
    client.sign_out_device().await.unwrap();
}

#[tokio::test]
async fn a_device_deleting_a_clip_it_may_not_is_forbidden() {
    let server = MockServer::start().await;
    Mock::given(method("DELETE"))
        .respond_with(ResponseTemplate::new(403))
        .mount(&server)
        .await;

    let error = signed_in(&server).delete_clip(Uuid::new_v4()).await.unwrap_err();
    assert!(
        matches!(
            error,
            ApiError::Forbidden(Problem {
                title: None,
                detail: None
            })
        ),
        "{error:?}"
    );
}

#[tokio::test]
async fn unauthorized_with_a_token_is_signed_out() {
    let error = plan_answering(401, json!(null)).await;
    assert!(matches!(error, ApiError::SignedOut), "{error:?}");
}

#[tokio::test]
async fn unauthorized_without_a_token_is_just_a_status() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .respond_with(ResponseTemplate::new(401))
        .mount(&server)
        .await;

    let error = client(&server).plan().await.unwrap_err();
    assert!(
        matches!(
            error,
            ApiError::Status {
                status: StatusCode::UNAUTHORIZED,
                ..
            }
        ),
        "{error:?}"
    );
}

#[tokio::test]
async fn upgrade_required_names_the_minimum_version() {
    let error = plan_answering(426, json!({ "error": "client_too_old", "minimum_version": "1.3" })).await;
    let ApiError::UpdateRequired { minimum_version } = error else {
        panic!("{error:?}")
    };
    assert_eq!(minimum_version.as_deref(), Some("1.3"));
}

#[tokio::test]
async fn upgrade_required_without_a_body_still_maps() {
    let server = MockServer::start().await;
    Mock::given(method("GET"))
        .respond_with(ResponseTemplate::new(426))
        .mount(&server)
        .await;

    // Even a request without a token is refused for its version.
    let error = client(&server).plan().await.unwrap_err();
    assert!(
        matches!(error, ApiError::UpdateRequired { minimum_version: None }),
        "{error:?}"
    );
}

#[tokio::test]
async fn a_full_plan_is_forbidden_with_its_title() {
    let error = plan_answering(403, problem("Plan Limit Reached", "Your plan keeps 100 clips.")).await;
    let ApiError::Forbidden(problem) = error else {
        panic!("{error:?}")
    };
    assert!(problem.is_plan_limit());
    assert_eq!(problem.message(), Some("Your plan keeps 100 clips."));
}

#[tokio::test]
async fn another_forbidden_is_not_a_full_plan() {
    let error = plan_answering(403, problem("Forbidden", "No.")).await;
    let ApiError::Forbidden(problem) = error else {
        panic!("{error:?}")
    };
    assert!(!problem.is_plan_limit());
}

#[tokio::test]
async fn not_found_maps() {
    let error = plan_answering(404, problem("Resource Not Found", "Clip x was not found")).await;
    assert!(matches!(error, ApiError::NotFound(_)), "{error:?}");
}

#[tokio::test]
async fn unprocessable_keeps_its_message() {
    let error = plan_answering(
        422,
        problem("Unprocessable Content", "The clip is longer than 60 seconds"),
    )
    .await;
    let ApiError::Unprocessable(problem) = error else {
        panic!("{error:?}")
    };
    assert_eq!(problem.message(), Some("The clip is longer than 60 seconds"));
}

#[tokio::test]
async fn unavailable_maps_and_is_transient() {
    let error = plan_answering(503, problem("Service Unavailable", "try again")).await;
    assert!(matches!(error, ApiError::Unavailable(_)), "{error:?}");
    assert!(error.is_transient());
}

#[tokio::test]
async fn other_statuses_keep_their_status() {
    let error = plan_answering(500, problem("An error occurred", "later")).await;
    let ApiError::Status { status, problem } = &error else {
        panic!("{error:?}")
    };
    assert_eq!(*status, StatusCode::INTERNAL_SERVER_ERROR);
    assert_eq!(problem.title.as_deref(), Some("An error occurred"));
    assert!(error.is_transient());

    let error = plan_answering(400, json!("Give the device a name")).await;
    assert!(
        matches!(&error, ApiError::Status { status: StatusCode::BAD_REQUEST, problem } if problem.message() == Some("Give the device a name")),
        "{error:?}"
    );
    assert!(!error.is_transient());

    let error = plan_answering(429, json!(null)).await;
    assert!(error.is_transient());
}

#[tokio::test]
async fn a_body_it_cant_read_is_a_decode_error() {
    let error = plan_answering(200, json!({ "tier": 3 })).await;
    assert!(matches!(error, ApiError::Decode(_)), "{error:?}");
}

#[tokio::test]
async fn no_server_is_a_network_error() {
    // A port nothing listens on any more (wiremock keeps its servers running in a pool).
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let uri = format!("http://{}", listener.local_addr().unwrap());
    drop(listener);

    let error = ApiClient::new(&uri, VERSION).unwrap().plan().await.unwrap_err();
    assert!(matches!(error, ApiError::Network(_)), "{error:?}");
    assert!(error.is_transient());
}

async fn poll_answering(status: u16, body: serde_json::Value) -> Result<DeviceTokenPoll, ApiError> {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/api/devices/token"))
        .and(body_json(json!({ "device_code": "dc" })))
        .respond_with(ResponseTemplate::new(status).set_body_json(body))
        .mount(&server)
        .await;
    client(&server).poll_device_token("dc").await
}

#[tokio::test]
async fn the_poll_answers_the_token_once_allowed() {
    let poll = poll_answering(200, json!({ "access_token": TOKEN, "token_type": "Bearer" }))
        .await
        .unwrap();
    let DeviceTokenPoll::Granted(token) = poll else {
        panic!("{poll:?}")
    };
    assert_eq!(token.access_token, TOKEN);
    assert!(!format!("{token:?}").contains(TOKEN), "the token stays out of Debug");
}

#[tokio::test]
async fn the_poll_reads_each_rfc_8628_code() {
    for (code, expected) in [
        ("authorization_pending", DeviceTokenErrorCode::AuthorizationPending),
        ("slow_down", DeviceTokenErrorCode::SlowDown),
        ("expired_token", DeviceTokenErrorCode::ExpiredToken),
        ("access_denied", DeviceTokenErrorCode::AccessDenied),
        ("invalid_grant", DeviceTokenErrorCode::Other("invalid_grant".into())),
    ] {
        let poll = poll_answering(400, json!({ "error": code })).await.unwrap();
        assert_eq!(poll, DeviceTokenPoll::Refused(expected), "{code}");
    }
}

#[tokio::test]
async fn the_poll_maps_other_failures() {
    let error = poll_answering(426, json!({ "error": "client_too_old", "minimum_version": "2.0" }))
        .await
        .unwrap_err();
    assert!(matches!(error, ApiError::UpdateRequired { .. }), "{error:?}");

    let error = poll_answering(400, json!("not an error object")).await.unwrap_err();
    assert!(
        matches!(
            error,
            ApiError::Status {
                status: StatusCode::BAD_REQUEST,
                ..
            }
        ),
        "{error:?}"
    );

    let error = poll_answering(429, json!(null)).await.unwrap_err();
    assert!(error.is_transient(), "{error:?}");
}

#[tokio::test]
async fn a_part_goes_to_its_url_without_the_token_and_answers_its_etag() {
    let server = MockServer::start().await;
    Mock::given(method("PUT"))
        .and(path("/bucket/uploads/clip"))
        .and(query_param("partNumber", "1"))
        .and(header("content-length", "5"))
        .respond_with(ResponseTemplate::new(200).insert_header("ETag", "\"abc\""))
        .expect(1)
        .mount(&server)
        .await;
    let client = signed_in(&server);

    let url = format!("{}/bucket/uploads/clip?partNumber=1&X-Amz-Signature=sig", server.uri());
    let etag = client.put_part(&url, b"hello".to_vec(), 5).await.unwrap();
    assert_eq!(etag, "\"abc\"");

    let request: &Request = &server.received_requests().await.unwrap()[0];
    assert!(request.headers.get("authorization").is_none());
    assert!(request.headers.get(CLIENT_HEADER).is_none());
    assert_eq!(request.body, b"hello");
}

#[tokio::test]
async fn a_part_storage_refuses_or_sends_no_etag_for_fails() {
    let server = MockServer::start().await;
    Mock::given(method("PUT"))
        .and(path("/refused"))
        .respond_with(ResponseTemplate::new(503))
        .mount(&server)
        .await;
    Mock::given(method("PUT"))
        .and(path("/no-etag"))
        .respond_with(ResponseTemplate::new(200))
        .mount(&server)
        .await;
    let client = signed_in(&server);

    let error = client
        .put_part(&format!("{}/refused", server.uri()), b"x".to_vec(), 1)
        .await
        .unwrap_err();
    assert!(
        matches!(error, ApiError::Storage(StatusCode::SERVICE_UNAVAILABLE)),
        "{error:?}"
    );
    assert!(error.is_transient());

    let error = client
        .put_part(&format!("{}/no-etag", server.uri()), b"x".to_vec(), 1)
        .await
        .unwrap_err();
    assert!(matches!(error, ApiError::MissingEtag), "{error:?}");
}
