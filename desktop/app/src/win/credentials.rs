//! The device token in Windows Credential Manager, as the generic credential "Reelshelf" for this user, with the
//! blob the .NET build wrote (UTF-16LE), so an existing sign-in carries over. It is never written anywhere else, logs
//! included.

use std::ffi::c_void;

use reelshelf_core::platform::CredentialStore;
use windows::Win32::Foundation::ERROR_NOT_FOUND;
use windows::Win32::Security::Credentials::{
    CRED_PERSIST_LOCAL_MACHINE, CRED_TYPE_GENERIC, CREDENTIALW, CredDeleteW, CredFree, CredReadW, CredWriteW,
};
use windows::core::{HSTRING, PWSTR};

use crate::platform::{CREDENTIAL_TARGET, credential_blob, token_from_blob};

pub struct CredentialManager;

fn not_found(error: &windows::core::Error) -> bool {
    error.code() == ERROR_NOT_FOUND.to_hresult()
}

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(Some(0)).collect()
}

impl CredentialStore for CredentialManager {
    fn read_token(&self) -> Option<String> {
        let mut credential: *mut CREDENTIALW = std::ptr::null_mut();
        // SAFETY: CredReadW writes a pointer it allocated, which is freed below with CredFree.
        if let Err(error) = unsafe {
            CredReadW(
                &HSTRING::from(CREDENTIAL_TARGET),
                CRED_TYPE_GENERIC,
                None,
                &mut credential,
            )
        } {
            if !not_found(&error) {
                tracing::warn!("Couldn't read the sign-in from Credential Manager: {error}");
            }
            return None;
        }
        // SAFETY: on success the pointer is a CREDENTIALW whose blob holds CredentialBlobSize bytes.
        let token = unsafe {
            let read = &*credential;
            if read.CredentialBlob.is_null() || read.CredentialBlobSize == 0 {
                None
            } else {
                token_from_blob(std::slice::from_raw_parts(
                    read.CredentialBlob,
                    read.CredentialBlobSize as usize,
                ))
            }
        };
        // SAFETY: frees the buffer CredReadW allocated, once.
        unsafe { CredFree(credential as *const c_void) };
        token
    }

    fn write_token(&self, token: &str) {
        let mut blob = credential_blob(token);
        let mut target = wide(CREDENTIAL_TARGET);
        let mut user = wide(&std::env::var("USERNAME").unwrap_or_default());
        let credential = CREDENTIALW {
            Type: CRED_TYPE_GENERIC,
            TargetName: PWSTR(target.as_mut_ptr()),
            CredentialBlobSize: blob.len() as u32,
            CredentialBlob: blob.as_mut_ptr(),
            Persist: CRED_PERSIST_LOCAL_MACHINE,
            UserName: PWSTR(user.as_mut_ptr()),
            ..CREDENTIALW::default()
        };
        // SAFETY: every pointer in `credential` points into a buffer that outlives the call.
        if let Err(error) = unsafe { CredWriteW(&credential, 0) } {
            tracing::warn!("Couldn't save the sign-in to Credential Manager: {error}");
        }
        // Don't leave the token in freed memory.
        blob.iter_mut().for_each(|byte| *byte = 0);
        std::hint::black_box(&blob);
    }

    fn delete_token(&self) {
        // SAFETY: a plain call with a NUL-terminated name.
        if let Err(error) = unsafe { CredDeleteW(&HSTRING::from(CREDENTIAL_TARGET), CRED_TYPE_GENERIC, None) }
            && !not_found(&error)
        {
            tracing::warn!("Couldn't remove the sign-in from Credential Manager: {error}");
        }
    }
}
