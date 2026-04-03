# Release Overview

## Что лежит в GitHub Release

Для каждого тега формируются архивы:

- `RtfTableExporter-vX.Y.Z-win-x64.zip`
- `RtfTableExporter-vX.Y.Z-linux-x64.zip`
- `RtfTableExporter-vX.Y.Z-linux-musl-x64.zip`
- `RtfTableExporter-vX.Y.Z-linux-arm64.zip`

Также прикладывается файл `SHA256SUMS.txt`.

## Что находится внутри архива

- основной бинарник
- `README.md`

## Как выпустить релиз

1. Запушить код в GitHub.
2. Создать тег:

```bash
git tag v1.2.3
git push origin v1.2.3
```

3. GitHub Actions автоматически:

- соберет все платформенные бинарники
- упакует их
- создаст GitHub Release
- прикрепит архивы и контрольные суммы

## Как работает автообновление в релизе

Релизные бинарники знают имя GitHub-репозитория, потому что workflow передает его при сборке. За счет этого приложение может безопасно проверять новые релизы без ручной настройки.
