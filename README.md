# RtfTableExporter

![RtfTableExporter logo](icon.png)

Кроссплатформенная консольная утилита на .NET для быстрого экспорта данных из `RTF` в `TXT`.

Из каждого входного файла берутся две табличные секции с данными по порядку документа: сначала доходы, затем расходы. У них отрезаются заголовки и служебные строки шапки, после чего в файл попадают строки с данными и строки `Итого по коду БК`.

## Что умеет

- Если входные пути не переданы, программа обрабатывает все `*.rtf`, которые лежат рядом с исполняемым файлом.
- Можно передать один файл, несколько файлов, папку или wildcard-маску.
- Если выходной путь не задан, `txt` сохраняются рядом с исполняемым файлом.
- Если выходной путь задан как папка, все результаты складываются туда.
- Если обрабатывается ровно один файл, можно задать точный путь к итоговому `txt`.
- Готовые `txt` перезаписываются. Если конкретный файл перезаписать нельзя, ошибка уходит в `stderr`, а остальные файлы продолжают обрабатываться.
- Разделитель по умолчанию: `|`.
- Разделитель можно заменить на любой свой, включая TAB.
- Итоговые `txt` по умолчанию записываются как `cp1251` и с переносами строк CRLF.
- Кодировку можно переключить вручную через `--encoding`: `cp1251`, `utf8`, `utf8-bom`.
- Для FoxPro можно использовать алиас `--foxpro`.
- Есть безопасная проверка обновлений из GitHub Releases. Ошибки обновления не валят основную обработку.
- Если до обработки найден новый релиз, программа обновляется и повторно запускается с теми же аргументами.

## Базовые примеры

```bash
RtfTableExporter
RtfTableExporter file1.rtf file2.rtf
RtfTableExporter --input file1.rtf --input file2.rtf
RtfTableExporter --input C:\data\
RtfTableExporter --input C:\data\*.rtf
RtfTableExporter --input file.rtf --output C:\result\
RtfTableExporter --input file.rtf --output C:\result\file.txt
RtfTableExporter --input file.rtf --delimiter ";"
RtfTableExporter --input file.rtf --delimiter "\t"
RtfTableExporter --input file.rtf --tab
RtfTableExporter --input file.rtf --foxpro
RtfTableExporter --input file.rtf --encoding utf8
RtfTableExporter --input file.rtf --encoding utf8-bom
```

## Логика вывода

`stdout`:

- `SAVED|<input>|<output>`
- `SUMMARY|<successCount>|<failureCount>`

`stderr`:

- `ERROR|<input>|<message>`
- `ERROR|<input>|<output>|<message>`
- `UPDATE|<message>`

## Коды возврата

- `0` все файлы обработаны успешно
- `1` ошибка аргументов
- `2` входные `rtf` не найдены
- `3` все обработки завершились ошибкой
- `4` фатальная ошибка приложения
- `5` частичный успех: часть файлов обработана, часть нет

## Вызов из другой программы

Рекомендуемый сценарий:

1. Запустить `exe` с набором входных файлов или без аргументов.
2. Читать `stdout` построчно и забирать строки `SAVED|...`.
3. Читать `stderr` как журнал ошибок.
4. Проверять `ExitCode`.

Для пакетного режима это удобнее, чем один-единственный путь в выводе, потому что на одном запуске может быть обработано сразу несколько файлов.

Если результат потом читает Visual FoxPro, можно ничего не добавлять: по умолчанию используется `cp1251`. Для явного режима можно запускать так:

```bash
RtfTableExporter --input file.rtf --foxpro
```

### Как запускать без окна

Сам `RtfTableExporter` не прячет консольное окно изнутри. Для запуска из другой программы правильнее скрывать окно на стороне вызывающего процесса.

Windows, .NET:

```csharp
using System.Diagnostics;

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = @"C:\tools\RtfTableExporter.exe",
        Arguments = @"""C:\data\report.rtf"" ""C:\out\report.txt""",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }
};

process.Start();
var stdout = process.StandardOutput.ReadToEnd();
var stderr = process.StandardError.ReadToEnd();
process.WaitForExit();
```

Windows, Visual FoxPro:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcCommand, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "C:\tools\RtfTableExporter.exe"
lcInput = "C:\data\report.rtf"
lcOutput = "C:\out\report.txt"
lcCommand = ["] + lcExe + [" --foxpro "] + lcInput + [" "] + lcOutput + ["]

* 0 = скрытое окно, .T. = ждать завершения
lnExitCode = loShell.Run(lcCommand, 0, .T.)
```

Windows, PowerShell:

```powershell
$p = Start-Process `
  -FilePath "C:\tools\RtfTableExporter.exe" `
  -ArgumentList '"C:\data\report.rtf" "C:\out\report.txt"' `
  -WindowStyle Hidden `
  -RedirectStandardOutput "C:\temp\rtf-out.log" `
  -RedirectStandardError "C:\temp\rtf-err.log" `
  -PassThru `
  -Wait

$p.ExitCode
```

Linux:

На Linux отдельного "консольного окна" обычно нет. Если не нужно занимать терминал, запускайте процесс в фоне, из сервиса или с перенаправлением вывода:

```bash
nohup ./RtfTableExporter "/home/user/data/report.rtf" "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

Если программа запускается из GUI-приложения, сервиса или другого процесса без терминала, отдельное окно и так не появится.

## Автообновление

Автообновление работает так:

- приложение смотрит последний GitHub Release
- ищет архив под текущую платформу
- скачивает его
- обновляет файлы до начала обработки
- повторно запускает новую версию с теми же аргументами и рабочей папкой
- проверяет наличие нового релиза на каждом запуске с коротким таймаутом

Ошибки обновления только логируются. Основная обработка `RTF` не прерывается.

Чтобы автообновление работало в локальной сборке, можно передать:

```bash
RtfTableExporter --github-repo owner/repo
```

или задать переменную среды:

```bash
RTF_TABLE_EXPORTER_GITHUB_REPOSITORY=owner/repo
```

В релизных бинарниках, собранных через GitHub Actions, репозиторий встраивается автоматически.

## Локальная публикация

PowerShell:

```powershell
.\publish-all.ps1 -Version 1.2.3 -GitHubRepository owner/repo
```

Bash:

```bash
./publish-all.sh Release 1.2.3 owner/repo
```

Скрипты публикуют self-contained single-file сборки для:

- `win-x64`
- `win-x86`
- `linux-x64`
- `linux-musl-x64`
- `linux-arm64`

## Документация

- Подробное использование: [docs/USAGE.md](docs/USAGE.md)
- Что попадает в релиз и как им пользоваться: [docs/RELEASE_OVERVIEW.md](docs/RELEASE_OVERVIEW.md)
