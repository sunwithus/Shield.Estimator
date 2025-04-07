# Audio Converter Service

Сервис для конвертации аудиофайлов с поддержкой нескольких библиотек (FFMpeg, Decoder.exe, NAudio). 
Предоставляет потокобезопасные методы для работы с файлами и байтовыми массивами.

## Особенности

- Поддержка нескольких движков: **FFMpeg**, **Decoder.exe**, **NAudio**
- Потокобезопасная реализация с использованием `SemaphoreSlim`
- Гибкая настройка параметров конвертации
- Фабрика для создания конвертеров (`AudioConverterFactory`)
- Работа с форматами: "WAVE_FILE", "RPE-LTP", "DAMPS", "GSM", "PCM-128", "QCELP-8", "EVRC", "QCELP-13", "ADPCM", "AMBE.HR_IRIDIUM", "A-LAW", "AMBE_INMARSAT_MM", "APC.HR_INMARSAT_B", "IMBE_INMARSAT_M",
            "AME", "ACELP_TETRA", "GSM.EFR_ABIS", "GSM.HR_ABIS", "GSM.AMR_ABIS", "GSM_ABIS", "LD-CELP", "E-QCELP", "ATC", "PSI-CELP", "AMBE.GMR1", "AMBE.GMR2", "AMBE.INMARSAT_BGAN", "ADM.UAV",
            "PCMA", "PCMU", "IPCMA", "IPCMU", "L8", "IL8", "L16", "IL16", "G.723.1", "G.726-32", "G.728", "G.729", "GSM.0610", "ILBC-13", "ILBC-15", "UMTS_AMR", "PDC.FR", "PDC.EFR", "PDC.HR",
            "IDEN.FR", "APCO-25", "RP-CELP", "IDEN.HR"
- Поддержка многоканального аудио

## Установка

1. Установите необходимые NuGet-пакеты:
   ```bash
   Install-Package FFMpegCore
   Install-Package NAudio
Добавьте сервисы в ваше приложение:

csharp
services.AddAudioConverterServices(Configuration);
Поместите бинарные файлы в соответствующие папки:

text
/bin
│
├── ffmpeg.exe   # FFMpeg binaries
├── ffprobe.exe  # FFMpeg binaries
└── decoder/     # decoder.exe и suppdll
Конфигурация
Пример настроек в appsettings.json:

json
"AudioConverterConfig": {
  "TargetSampleRate": 16000,
  "TargetBitRate": 256000,
  "TempDirectory": "/tmp/audio_converter"
}
Использование
Инициализация
csharp
var converter = factory.CreateConverter(ConverterType.FFMpeg);
Примеры методов
csharp
// Конвертация файла в MemoryStream
var stream = await converter.ConvertFileToStreamAsync("input.wav");

// Конвертация byte[] в файл
await converter.ConvertByteArrayToFileAsync(
    leftChannelData, 
    rightChannelData, 
    "output.wav",
    recordType: "PCMA",
    eventCode: "PCMA");

// Получение PCM данных из файла
var (duration, left, right) = await converter.ConvertFileToByteArrayAsync("input.mp3");
Классы и интерфейсы

Компонент	            Описание
IAudioConverter	        Основной интерфейс конвертера
AudioConverterFactory	Фабрика для создания экземпляров
FFMpegConverter 	    Реализация через FFMpegCore
DecoderConverter	    Конвертер через decoder.exe
NAudioConverter     	Реализация с использованием NAudio

Важные замечания
Для работы FFMpegConverter требуется:

Библиотеки FFMpeg в папке ffmpeg

Временная директория с правами на запись

Требуется внешний decoder.exe

Все операции используют временные файлы в TempDirectory

## Интерфейсы

public interface IAudioConverter
{
    Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName);
    Task<(int Duration, byte[]? Left, byte[]? Right)> ConvertFileToByteArrayAsync(string inputFileName);
    Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[]? audioDataLeft, byte[]? audioDataRight = null, string recordType = "", string eventCode = "");
    Task ConvertByteArrayToFileAsync(byte[]? audioDataLeft, byte[]? audioDataRight, string audioFilePath, string recordType = "", string eventCode = "");
}