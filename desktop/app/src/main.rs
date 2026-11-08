//! `reelshelf`: the uploader's tray app.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    reelshelf_app::shell::run();
}
