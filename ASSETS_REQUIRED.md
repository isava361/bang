# Asset Requirements for Bang Online

Place all assets under `wwwroot/assets/` so the web UI can load them at runtime. The paths below are referenced directly in code and styles.

## Tabletop Layout
- **Background image**: `wwwroot/assets/backgrounds/table.jpg` (recommended: 1920x1080 or larger, JPG).

## Cards
- **Card fronts (PNG, 512x768)**:
  - `wwwroot/assets/cards/bang.png`
  - `wwwroot/assets/cards/beer.png`
  - `wwwroot/assets/cards/gatling.png`
  - `wwwroot/assets/cards/stagecoach.png`
  - `wwwroot/assets/cards/cat_balou.png`
  - `wwwroot/assets/cards/indians.png`
  - `wwwroot/assets/cards/duel.png`
  - `wwwroot/assets/cards/panic.png`
  - `wwwroot/assets/cards/saloon.png`
  - `wwwroot/assets/cards/wells_fargo.png`
  - `wwwroot/assets/cards/general_store.png`
- **Card back (PNG, 512x768)**: `wwwroot/assets/cards/card_back.png` (reserved for future deck UI).

## Characters
- **Portraits (PNG, 256x256)**:
  - `wwwroot/assets/characters/lucky_duke.png`
  - `wwwroot/assets/characters/slab_the_killer.png`
  - `wwwroot/assets/characters/el_gringo.png`
  - `wwwroot/assets/characters/suzy_lafayette.png`
  - `wwwroot/assets/characters/rose_doolan.png`
  - `wwwroot/assets/characters/jesse_jones.png`
  - `wwwroot/assets/characters/bart_cassidy.png`
  - `wwwroot/assets/characters/paul_regret.png`
  - `wwwroot/assets/characters/calamity_janet.png`
  - `wwwroot/assets/characters/kit_carlson.png`
  - `wwwroot/assets/characters/willy_the_kid.png`
  - `wwwroot/assets/characters/sid_ketchum.png`
  - `wwwroot/assets/characters/vulture_sam.png`
  - `wwwroot/assets/characters/pedro_ramirez.png`

## Optional UI Extras
- **Turn indicator icon (PNG, 128x128)**: `wwwroot/assets/ui/turn_indicator.png`
- **Button icons (PNG, 64x64)**: `wwwroot/assets/ui/start.png`, `wwwroot/assets/ui/end_turn.png`, `wwwroot/assets/ui/chat.png`
- **Health tokens (PNG, 64x64)**: `wwwroot/assets/ui/health_token.png`

## Audio (Optional)
- **Sound effects**: `wwwroot/assets/audio/` (gunshot, card draw, heal, etc.) in `.mp3` or `.wav`.
- **Ambient loop**: `wwwroot/assets/audio/ambient_saloon.mp3`.
