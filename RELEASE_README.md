# RtfTableExporter

Утилита конвертирует `RTF` и `DOCX` в `TXT`.

Что делает:

- автоматически определяет формы `0531857` и `0503152`
- для формы `0531857` читает секции доходов и расходов
- для формы `0503152` читает единую таблицу отчета
- для неизвестных форм выгружает все содержательные таблицы
- по умолчанию пишет `txt` в `cp1251` с переносами строк `CRLF`
- умеет `--encoding cp1251`, `--encoding utf8`, `--encoding utf8-bom`
- если вход не задан, обрабатывает все `*.rtf` и `*.docx` рядом с программой
- если выход не задан, сохраняет `*.txt` рядом с программой
- по умолчанию пишет файловый лог `RtfTableExporter.log` рядом с программой
- умеет самообновляться из GitHub Releases

## Windows

Все `RTF` и `DOCX` рядом с программой:

```powershell
.\RtfTableExporter.exe
```

Один файл:

```powershell
.\RtfTableExporter.exe --input "C:\data\report.rtf" --output "C:\out\report.txt"
```

Папка:

```powershell
.\RtfTableExporter.exe --input "C:\data\" --output "C:\out\"
```

FoxPro-режим:

```powershell
.\RtfTableExporter.exe --foxpro --input "C:\data\report.rtf" --output "C:\out\report.txt"
```

## Linux

Сначала дайте право на запуск:

```bash
chmod +x ./RtfTableExporter
```

Один файл:

```bash
./RtfTableExporter --input "/home/user/data/report.rtf" --output "/home/user/out/report.txt"
```

Папка:

```bash
./RtfTableExporter --input "/home/user/data/" --output "/home/user/out/"
```

FoxPro-совместимая кодировка:

```bash
./RtfTableExporter --foxpro --input "/home/user/data/report.rtf" --output "/home/user/out/report.txt"
```

## Логи

По умолчанию создается или дописывается файл:

```text
RtfTableExporter.log
```

по соседству с программой.

Полезные ключи:

```bash
RtfTableExporter --log-path C:\logs\RtfTableExporter.log
RtfTableExporter --no-file-log
```

Логирование не должно ломать обработку: если запись лога не удалась, конвертация продолжается.

## FoxPro на Linux/Wine

Рекомендуемый сценарий:

- использовать Windows-сборку `RtfTableExporter.exe`
- для FoxPro/Wine брать `win-x86`
- запускать через тот же `WINEPREFIX`, где работает FoxPro
- передавать явные пути `Q:\...` или `Z:\...`
- всегда передавать `--input`, `--output`, `--foxpro`, `--no-update-check`

Пример:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcOutLog, lcErrLog, lcCmd, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "Z:\opt\RtfTableExporter\RtfTableExporter.exe"
lcInput = "Q:\Только для обмена документами\report.rtf"
lcOutput = "Q:\Только для обмена документами\report.txt"
lcOutLog = "Z:\tmp\RtfTableExporter-out.log"
lcErrLog = "Z:\tmp\RtfTableExporter-err.log"

lcCmd = [cmd /c ""] + lcExe + ;
    [" --no-update-check --foxpro --input "] + lcInput + ;
    [" --output "] + lcOutput + ;
    [" > "] + lcOutLog + ;
    [" 2> "] + lcErrLog + [""]

lnExitCode = loShell.Run(lcCmd, 0, .T.)
```

Если FoxPro “ничего не делает”, проверьте:

1. Используется ли `win-x86`, а не `win-x64`.
2. Совпадает ли `WINEPREFIX` у FoxPro и ручного запуска.
3. Существуют ли нужные маппинги дисков `Q:` и `Z:`.
4. Есть ли записи в `RtfTableExporter.log`, `RtfTableExporter-out.log` и `RtfTableExporter-err.log`.

## Без окна

Если нужно скрыть консоль, делайте это на стороне вызывающей программы.

Visual FoxPro:

```foxpro
LOCAL loShell, lcCmd, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcCmd = ["] + FULLPATH("RtfTableExporter.exe") + ;
    [" --foxpro --no-update-check --input C:\data\report.rtf --output C:\out\report.txt"]

lnExitCode = loShell.Run(lcCmd, 0, .T.)
```

PowerShell:

```powershell
Start-Process `
  -FilePath ".\RtfTableExporter.exe" `
  -ArgumentList '--foxpro --no-update-check --input "C:\data\report.rtf" --output "C:\out\report.txt"' `
  -WindowStyle Hidden
```

Linux:

```bash
nohup ./RtfTableExporter --no-update-check --input "/home/user/data/report.rtf" --output "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

## Вывод в консоль

`stdout`:

- `SAVED|<input>|<output>`
- `SUMMARY|<successCount>|<failureCount>`

`stderr`:

- `ERROR|<input>|<message>`
- `ERROR|<input>|<output>|<message>`
- `UPDATE|<message>`
