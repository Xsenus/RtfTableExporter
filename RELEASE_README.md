# RtfTableExporter

Утилита конвертирует `RTF` в `TXT`.

Что делает:

- читает секции `1.Доходы` и `2. Расходы`
- пишет строки с данными и строки `Итого по коду БК`
- не пишет шапку таблиц и общую строку `Всего`
- если вход не задан, обрабатывает все `*.rtf` рядом с программой
- если выход не задан, сохраняет `*.txt` рядом с программой

## Windows

Все `RTF` рядом с программой:

```powershell
.\RtfTableExporter.exe
```

Один файл:

```powershell
.\RtfTableExporter.exe "C:\data\report.rtf"
```

Несколько файлов:

```powershell
.\RtfTableExporter.exe "C:\data\a.rtf" "C:\data\b.rtf"
```

Папка с входными файлами:

```powershell
.\RtfTableExporter.exe --input "C:\data\"
```

Маска:

```powershell
.\RtfTableExporter.exe --input "C:\data\*.rtf"
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

Все `RTF` рядом с программой:

```bash
./RtfTableExporter
```

Один файл:

```bash
./RtfTableExporter "/home/user/data/report.rtf"
```

Несколько файлов:

```bash
./RtfTableExporter "/home/user/data/a.rtf" "/home/user/data/b.rtf"
```

Папка с входными файлами:

```bash
./RtfTableExporter --input "/home/user/data/"
```

Маска:

```bash
./RtfTableExporter --input "/home/user/data/*.rtf"
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
```

## Вывод в консоль

`stdout`:

- `SAVED|<input>|<output>`
- `SUMMARY|<successCount>|<failureCount>`

`stderr`:

- `ERROR|<input>|<message>`
- `ERROR|<input>|<output>|<message>`
- `UPDATE|<message>`
