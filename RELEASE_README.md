# RtfTableExporter

Утилита конвертирует `RTF` и `DOCX` в `TXT`.

Что делает:

- автоматически определяет поддерживаемую форму отчета: `0531857` или `0503152`
- для формы `0531857` читает секции `1.Доходы` и `2. Расходы`
- для формы `0503152` читает единую таблицу отчета
- для неизвестных форм выгружает все содержательные таблицы как обычный текст
- пишет строки с данными, для формы `0531857` также сохраняет строки `Итого по коду БК`
- старый бинарный `.doc` напрямую не читает; сохраняйте его как `.docx` или `.rtf`
- не пишет шапку таблиц и общую строку `Всего`
- по умолчанию сохраняет `txt` как `cp1251` и с переносами строк CRLF
- позволяет выбрать `--encoding cp1251`, `--encoding utf8` или `--encoding utf8-bom`
- если вход не задан, обрабатывает все `*.rtf` и `*.docx` рядом с программой
- если выход не задан, сохраняет `*.txt` рядом с программой
- проверяет новые релизы GitHub на каждом запуске
- если найдено обновление, сначала обновляется, а затем запускается снова с теми же аргументами

## Windows

Все `RTF` и `DOCX` рядом с программой:

```powershell
.\RtfTableExporter.exe
```

Один файл:

```powershell
.\RtfTableExporter.exe "C:\data\report.rtf"
```

Несколько файлов:

```powershell
.\RtfTableExporter.exe "C:\data\a.rtf" "C:\data\b.docx"
```

Папка с входными файлами:

```powershell
.\RtfTableExporter.exe --input "C:\data\"
```

Маска:

```powershell
.\RtfTableExporter.exe --input "C:\data\*.rtf"
```

Маска DOCX:

```powershell
.\RtfTableExporter.exe --input "C:\data\*.docx"
```

Папка для результата:

```powershell
.\RtfTableExporter.exe --input "C:\data\" --output "C:\out\"
```

Один конкретный выходной файл:

```powershell
.\RtfTableExporter.exe "C:\data\report.rtf" --output "C:\out\report.txt"
```

Свой разделитель:

```powershell
.\RtfTableExporter.exe "C:\data\report.rtf" --delimiter ";"
.\RtfTableExporter.exe "C:\data\report.rtf" --tab
```

## Linux

Сначала дайте право на запуск:

```bash
chmod +x ./RtfTableExporter
```

Для автообновления текущий пользователь должен иметь право записи в папку, где лежит `RtfTableExporter`.

Все `RTF` и `DOCX` рядом с программой:

```bash
./RtfTableExporter
```

Один файл:

```bash
./RtfTableExporter "/home/user/data/report.rtf"
```

Несколько файлов:

```bash
./RtfTableExporter "/home/user/data/a.rtf" "/home/user/data/b.docx"
```

Папка с входными файлами:

```bash
./RtfTableExporter --input "/home/user/data/"
```

Маска:

```bash
./RtfTableExporter --input "/home/user/data/*.rtf"
```

Маска DOCX:

```bash
./RtfTableExporter --input "/home/user/data/*.docx"
```

Папка для результата:

```bash
./RtfTableExporter --input "/home/user/data/" --output "/home/user/out/"
```

Один конкретный выходной файл:

```bash
./RtfTableExporter "/home/user/data/report.rtf" --output "/home/user/out/report.txt"
```

Свой разделитель:

```bash
./RtfTableExporter "/home/user/data/report.rtf" --delimiter ";"
./RtfTableExporter "/home/user/data/report.rtf" --tab
./RtfTableExporter "/home/user/data/report.rtf" --foxpro
./RtfTableExporter "/home/user/data/report.rtf" --encoding utf8-bom
```

## Запуск без окна

`RtfTableExporter` не скрывает консоль сам по себе. Если его запускает другая программа, окно нужно скрывать на стороне этой программы.

Visual FoxPro:

```foxpro
LOCAL loShell, lcCommand, lnExitCode

loShell = CREATEOBJECT("WScript.Shell")
lcCommand = ["] + FULLPATH("RtfTableExporter.exe") + [" --foxpro "] + ;
    "C:\data\report.rtf" + [" "] + ;
    "C:\out\report.txt" + ["]

* 0 = скрытое окно, .T. = ждать завершения
lnExitCode = loShell.Run(lcCommand, 0, .T.)
```

.NET:

```csharp
var psi = new ProcessStartInfo
{
    FileName = @"C:\tools\RtfTableExporter.exe",
    Arguments = @"--foxpro ""C:\data\report.rtf"" ""C:\out\report.txt""",
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
```

Linux:

```bash
nohup ./RtfTableExporter "/home/user/data/report.rtf" "/home/user/out/report.txt" >rtf-out.log 2>rtf-err.log &
```

## Вывод в консоль

`stdout`:

- `SAVED|<input>|<output>`
- `SUMMARY|<successCount>|<failureCount>`

`stderr`:

- `ERROR|<input>|<message>`
- `ERROR|<input>|<output>|<message>`
- `UPDATE|<message>`
