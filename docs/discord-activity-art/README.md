# Discord Activity art

Art uploaded to the Discord Developer Portal for the watch room Activity (ADR-0006). All three images are 1024×576 PNGs.

- `activity-cover.png` is the cover art, the main image on the Activity Shelf: the title with a shelf of books behind it.
- `activity-background.png` is the Grid view background overlay: bookcases at the edges and an empty centre so the UI doesn't clash with it.
- `activity-invite.png` is the Rich Presence invite image. Discord lays the invite card over it, with the app icon, name and player count on the left and a full-width Join button along the bottom (`invite-layout-reference.png`), so the books sit top-right and the rest stays quiet.

They are drawn in the style of `frontend/public/favicon.svg` (banded books, the last one leaning) in the site's dark palette and Fraunces type.

## Editing

The images are rendered from `source/`. `books.js` draws the shelves; each `shelf(...)` call takes a `seed` that picks its books, so changing a seed reshuffles that row. The fonts are copied in so the pages render on their own.

Re-render after a change:

```sh
cd docs/discord-activity-art/source
for f in background cover invite; do
  google-chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
    --window-size=1024,576 --virtual-time-budget=2000 \
    --screenshot="$PWD/../activity-$f.png" "file://$PWD/$f.html"
done
```

Then upload the new PNGs in the Developer Portal under the Activity's art assets.
