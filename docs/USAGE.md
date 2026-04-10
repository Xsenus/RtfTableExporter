# Usage

## Что считается входом

Поддерживаются:

- одиночный `.rtf` или `.docx`
- список `.rtf` и `.docx`
- папка с `.rtf` и `.docx`
- wildcard-маска, например `C:\data\*.rtf` или `C:\data\*.docx`

Если входы не переданы, программа берет все `*.rtf` и `*.docx` из папки, где лежит исполняемый файл.

Старый бинарный формат `.doc` напрямую не поддерживается. Такой файл нужно сохранить как `.docx` или `.rtf`.

## Что считается выходом

Если `--output` не задан:

- все `txt` создаются рядом с исполняемым файлом

Если `--output` указывает на папку:

- все результаты складываются в эту папку

Если `--output` указывает на файл:

- это допустимо только для одного входного файла

## Разделитель

По умолчанию:

```text
|
```

Примеры:

```bash
RtfTableExporter file.rtf --delimiter ";"
RtfTableExporter file.rtf --delimiter "\t"
RtfTableExporter file.rtf --tab
RtfTableExporter file.rtf --delimiter "||"
```

## Кодировка вывода

По умолчанию:

```text
cp1251
```

Поддерживаются:

- `cp1251`
- `utf8`
- `utf8-bom`

Примеры:

```bash
RtfTableExporter file.rtf --foxpro
RtfTableExporter file.rtf --encoding cp1251
RtfTableExporter file.rtf --encoding utf8
RtfTableExporter file.rtf --encoding utf8-bom
```

Если результат читает Visual FoxPro, рекомендуемый режим: `cp1251`. Он уже используется по умолчанию, а `--foxpro` оставлен как явный алиас.

## Файловый лог

По умолчанию программа пишет лог в файл:

```text
RtfTableExporter.log
```

рядом с программой.

В лог попадают:

- старт приложения, версия, PID, рабочая папка и аргументы
- параметры запуска
- результаты поиска входных файлов
- старт и результат обработки каждого файла
- ошибки конвертации
- этапы автообновления
- итоговый код завершения

Формат строки:

```text
2026-04-10T13:45:12.3456789+07:00 | INFO | Starting file conversion. | inputPath=C:\data\report.rtf
```

Ключи:

```bash
RtfTableExporter file.rtf --log-path C:\logs\RtfTableExporter.log
RtfTableExporter file.rtf --no-file-log
```

Важно:

- ошибки записи в лог не останавливают обработку
- при размере больше 5 МБ текущий лог переносится в `RtfTableExporter.log.1`
- `stderr` и `stdout` продолжают работать независимо от файла лога

## Примеры вызова

Обработать все `.rtf` и `.docx` рядом с программой:

```bash
RtfTableExporter
```

Обработать список файлов:

```bash
RtfTableExporter a.rtf b.docx c.rtf
```

Обработать папку:

```bash
RtfTableExporter --input C:\data\
```

Сохранить все результаты в отдельную папку:

```bash
RtfTableExporter a.rtf b.rtf --output C:\result\
```

Сохранить один конкретный файл в конкретное имя:

```bash
RtfTableExporter a.rtf --output C:\result\a.txt
```

Обработать wildcard:

```bash
RtfTableExporter --input C:\data\*.rtf
```

Обработать DOCX по wildcard:

```bash
RtfTableExporter --input C:\data\*.docx
```

Отключить проверку обновлений для этого запуска:

```bash
RtfTableExporter a.rtf --no-update-check
```

Проверка обновлений выполняется до обработки файлов. Если найден новый релиз, программа пытается обновиться и затем продолжает запуск с теми же аргументами. Если takeover новой версии не подтвержден, текущая версия продолжает обработку сама.

## Что попадает в txt

Для каждого `RTF` или `DOCX`:

1. Программа автоматически определяет поддерживаемую форму отчета: `0531857` или `0503152`.
2. Для формы `0531857` ищутся секции `1.Доходы` и `2. Расходы`; после каждой секции берется первая верхнеуровневая таблица с данными.
3. Для формы `0503152` берется единая таблица отчета.
4. Если форма не распознана, выгружаются все содержательные таблицы по порядку документа.
5. Для распознанных форм шапки таблиц и служебные заголовки не экспортируются.
6. Для формы `0531857` сохраняются строки данных и строки `Итого по коду БК`.
7. Общая строка `Всего` не экспортируется для распознанных форм.
8. В общем режиме строки таблиц пишутся как есть.
9. Пустые колонки сохраняются и не схлопываются.
10. Строки по умолчанию сохраняются как `cp1251`.
11. Переносы строк всегда записываются как `CRLF`, одинаково на Windows и Linux.

## Поведение при ошибках

- Если один файл не найден, остальные продолжают обрабатываться.
- Если `txt` нельзя перезаписать, остальные файлы продолжают обрабатываться.
- Ошибки по файлам уходят в `stderr` и в файловый лог, если он включен.
- Ошибки логирования не влияют на обработку.
- Общий код завершения показывает, был ли частичный успех.

## Запуск без консоли

Само приложение не скрывает окно консоли. Если его запускает другая программа, скрывать окно нужно на стороне вызывающего процесса.

Windows, .NET:

```csharp
using System.Diagnostics;

var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = @"C:\tools\RtfTableExporter.exe",
        Arguments = @"--foxpro --no-update-check --input ""C:\data\report.rtf"" --output ""C:\out\report.txt""",
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

Visual FoxPro:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcCommand, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "C:\tools\RtfTableExporter.exe"
lcInput = "C:\data\report.rtf"
lcOutput = "C:\out\report.txt"
lcCommand = ["] + lcExe + [" --foxpro --no-update-check --input "] + lcInput + ;
    [" --output "] + lcOutput + [""]

* 0 = скрытое окно, .T. = ждать завершения
lnExitCode = loShell.Run(lcCommand, 0, .T.)
```

PowerShell:

```powershell
$p = Start-Process `
  -FilePath "C:\tools\RtfTableExporter.exe" `
  -ArgumentList '--foxpro --no-update-check --input "C:\data\report.rtf" --output "C:\out\report.txt"' `
  -WindowStyle Hidden `
  -RedirectStandardOutput "C:\temp\rtf-out.log" `
  -RedirectStandardError "C:\temp\rtf-err.log" `
  -PassThru `
  -Wait

$p.ExitCode
```

Linux:

```bash
nohup ./RtfTableExporter --no-update-check --input "/home/user/data/report.rtf" --output "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

На Linux отдельного окна консоли обычно нет. Если запуск идет из GUI-приложения, сервиса или другого процесса без терминала, отдельное окно и так не появится.

## FoxPro на Linux/Wine

Рекомендуемый сценарий для FoxPro под Linux:

1. Использовать Windows-сборку `RtfTableExporter.exe`.
2. Для FoxPro/Wine брать именно `win-x86`.
3. Запускать через тот же `WINEPREFIX`, где работает FoxPro.
4. Передавать пути только в формате Wine, например `Q:\...` или `Z:\...`.
5. Всегда задавать `--input`, `--output`, `--foxpro`, `--no-update-check`.
6. Логи `stdout` и `stderr` перенаправлять в отдельные файлы.

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

Быстрая диагностика:

```foxpro
loShell.Run([cmd /c dir "Q:\Только для обмена документами" > "Z:\tmp\fx-dir.txt" 2>&1], 0, .T.)
loShell.Run([cmd /c "] + lcExe + [" --help > "Z:\tmp\fx-help.txt" 2>&1], 0, .T.)
```

Если вручную приложение работает, а из FoxPro нет, проверьте:

- тот ли `WINEPREFIX` используется
- существует ли диск `Q:` внутри этого prefix
- взят ли `win-x86`, а не `win-x64`
- передаются ли явные `--input` и `--output`
- что написано в `RtfTableExporter.log`, `RtfTableExporter-out.log` и `RtfTableExporter-err.log`
