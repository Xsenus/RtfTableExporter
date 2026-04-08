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

Проверка обновлений выполняется до обработки файлов. Если найден новый релиз, программа обновляется и автоматически запускается снова с теми же аргументами.

## Что попадает в txt

Для каждого `RTF` или `DOCX`:

1. Программа автоматически определяет поддерживаемую форму отчета: `0531857` или `0503152`.
2. Для формы `0531857` ищутся секции `1.Доходы` и `2. Расходы`; после каждой секции берется первая верхнеуровневая таблица с данными.
3. Для формы `0503152` берется единая таблица отчета.
4. Если форма не распознана, включается общий режим: выгружаются все содержательные таблицы по порядку документа.
5. Для распознанных форм шапки таблиц и служебные заголовки не экспортируются.
6. Для распознанных форм оставляются строки данных; для формы `0531857` также сохраняются строки `Итого по коду БК`.
7. Общая строка `Всего` не экспортируется для распознанных форм.
8. В общем режиме строки таблиц пишутся как есть, включая заголовки таблиц.
9. Строки по умолчанию сохраняются как `cp1251`.
10. При необходимости кодировку можно переключить на `utf8` или `utf8-bom`.
11. Переносы строк всегда записываются как CRLF, одинаково на Windows и Linux.

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
lcCommand = ["] + lcExe + [" --foxpro "] + lcInput + [" "] + lcOutput + ["]

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
