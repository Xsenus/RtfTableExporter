# Usage

## Что считается входом

Поддерживаются:

- одиночный `.rtf`
- список `.rtf`
- папка с `.rtf`
- wildcard-маска, например `C:\data\*.rtf`

Если входы не переданы, программа берет все `*.rtf` из папки, где лежит исполняемый файл.

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

## Примеры вызова

Обработать все `.rtf` рядом с программой:

```bash
RtfTableExporter
```

Обработать список файлов:

```bash
RtfTableExporter a.rtf b.rtf c.rtf
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

Отключить проверку обновлений для этого запуска:

```bash
RtfTableExporter a.rtf --no-update-check
```

Проверка обновлений выполняется до обработки файлов. Если найден новый релиз, программа обновляется и автоматически запускается снова с теми же аргументами.

## Что попадает в txt

Для каждого `RTF`:

1. Ищутся секции `1.Доходы` и `2. Расходы`.
2. После каждой секции берется первая верхнеуровневая таблица с данными.
3. У каждой секции отбрасывается шапка таблицы.
4. Оставляются строки данных и строки `Итого по коду БК`.
5. Общая строка `Всего` не экспортируется.
6. Строки сохраняются как UTF-8 с BOM.
7. Переносы строк всегда записываются как CRLF, одинаково на Windows и Linux.

## Поведение при ошибках

- Если один файл не найден, остальные продолжают обрабатываться.
- Если `txt` нельзя перезаписать, остальные продолжают обрабатываться.
- Ошибки по файлам уходят в `stderr`.
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

Visual FoxPro:

```foxpro
LOCAL loShell, lcExe, lcInput, lcOutput, lcCommand, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcExe = "C:\tools\RtfTableExporter.exe"
lcInput = "C:\data\report.rtf"
lcOutput = "C:\out\report.txt"
lcCommand = ["] + lcExe + [" "] + lcInput + [" "] + lcOutput + ["]

* 0 = скрытое окно, .T. = ждать завершения
lnExitCode = loShell.Run(lcCommand, 0, .T.)
```

PowerShell:

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

```bash
nohup ./RtfTableExporter "/home/user/data/report.rtf" "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

На Linux отдельного окна консоли обычно нет. Если запуск идет из GUI-приложения, сервиса или другого процесса без терминала, отдельное окно и так не появится.
