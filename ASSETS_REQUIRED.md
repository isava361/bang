# Требования к ассетам для Bang Online

Разместите все ассеты в `wwwroot/assets/`, чтобы веб-интерфейс мог загружать их во время работы. Пути ниже используются напрямую в коде и стилях.

## Макет стола
- **Фоновое изображение**: `wwwroot/assets/backgrounds/table.jpg` (рекомендуется: 1920x1080 или больше, JPG).

## Карты
- **Лицевые стороны карт (PNG, 512x768)**:
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
- **Рубашка карты (PNG, 512x768)**: `wwwroot/assets/cards/card_back.png` (зарезервировано для будущего интерфейса колоды).

## Персонажи
- **Портреты (PNG, 256x256)**:
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

## Дополнительные элементы интерфейса (опционально)
- **Иконка индикатора хода (PNG, 128x128)**: `wwwroot/assets/ui/turn_indicator.png`
- **Иконки кнопок (PNG, 64x64)**: `wwwroot/assets/ui/start.png`, `wwwroot/assets/ui/end_turn.png`, `wwwroot/assets/ui/chat.png`
- **Жетоны здоровья (PNG, 64x64)**: `wwwroot/assets/ui/health_token.png`

## Аудио (опционально)
- **Звуковые эффекты**: `wwwroot/assets/audio/` (выстрел, добор карт, лечение и т.д.) в `.mp3` или `.wav`.
- **Фоновая петля**: `wwwroot/assets/audio/ambient_saloon.mp3`.
