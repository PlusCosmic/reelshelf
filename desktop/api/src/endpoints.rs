//! The endpoints the uploader calls, by method and path template as the OpenAPI document names them. The client
//! builds its URLs from these, and the contract test checks each against the document.

use reqwest::Method;

/// One API endpoint: its `operationId`, method and path template (`{name}` for a path parameter).
#[derive(Debug, Clone)]
pub struct Endpoint {
    pub operation_id: &'static str,
    pub method: Method,
    pub path: &'static str,
}

impl Endpoint {
    /// The path with each `{name}` replaced by its value.
    pub fn path_with(&self, params: &[(&str, &str)]) -> String {
        let mut path = self.path.to_owned();
        for (name, value) in params {
            path = path.replace(&format!("{{{name}}}"), value);
        }
        path
    }
}

/// `POST /api/devices/authorize`: starts a device sign-in.
pub const AUTHORIZE_DEVICE: Endpoint = Endpoint {
    operation_id: "AuthorizeDevice",
    method: Method::POST,
    path: "/api/devices/authorize",
};

/// `POST /api/devices/token`: polls for the device's token.
pub const GET_DEVICE_TOKEN: Endpoint = Endpoint {
    operation_id: "GetDeviceToken",
    method: Method::POST,
    path: "/api/devices/token",
};

/// `DELETE /api/devices/current`: revokes the token the request carries.
pub const SIGN_OUT_CURRENT_DEVICE: Endpoint = Endpoint {
    operation_id: "SignOutCurrentDevice",
    method: Method::DELETE,
    path: "/api/devices/current",
};

/// `GET /api/games/categories`: the account's books.
pub const GET_GAME_CATEGORIES: Endpoint = Endpoint {
    operation_id: "GetUserGameCategories",
    method: Method::GET,
    path: "/api/games/categories",
};

/// `GET /api/me/plan`: the account's plan and its limits.
pub const GET_MY_PLAN: Endpoint = Endpoint {
    operation_id: "GetMyPlan",
    method: Method::GET,
    path: "/api/me/plan",
};

/// `POST /api/clips/categories/{categoryId}/videos`: reserves a clip and answers where its upload goes.
pub const CREATE_VIDEO: Endpoint = Endpoint {
    operation_id: "CreateVideo",
    method: Method::POST,
    path: "/api/clips/categories/{categoryId}/videos",
};

/// `DELETE /api/clips/videos/{clipId}`: with a device token, only a clip still awaiting its upload.
pub const DELETE_CLIP: Endpoint = Endpoint {
    operation_id: "DeleteClip",
    method: Method::DELETE,
    path: "/api/clips/videos/{clipId}",
};

/// `POST /api/clips/videos/{clipId}/upload/parts`: presigned URLs for some parts.
pub const PRESIGN_UPLOAD_PARTS: Endpoint = Endpoint {
    operation_id: "PresignUploadParts",
    method: Method::POST,
    path: "/api/clips/videos/{clipId}/upload/parts",
};

/// `GET /api/clips/videos/{clipId}/upload/parts?uploadId=`: the parts storage holds, for resuming.
pub const LIST_UPLOAD_PARTS: Endpoint = Endpoint {
    operation_id: "ListUploadParts",
    method: Method::GET,
    path: "/api/clips/videos/{clipId}/upload/parts",
};

/// `POST /api/clips/videos/{clipId}/upload/complete`: joins the parts and checks the file.
pub const COMPLETE_UPLOAD: Endpoint = Endpoint {
    operation_id: "CompleteUpload",
    method: Method::POST,
    path: "/api/clips/videos/{clipId}/upload/complete",
};

/// Every endpoint the client calls.
pub const ALL: &[Endpoint] = &[
    AUTHORIZE_DEVICE,
    GET_DEVICE_TOKEN,
    SIGN_OUT_CURRENT_DEVICE,
    GET_GAME_CATEGORIES,
    GET_MY_PLAN,
    CREATE_VIDEO,
    DELETE_CLIP,
    PRESIGN_UPLOAD_PARTS,
    LIST_UPLOAD_PARTS,
    COMPLETE_UPLOAD,
];
