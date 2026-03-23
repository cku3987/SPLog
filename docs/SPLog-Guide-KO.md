# SPLog 사용 가이드

## 개요

SPLog는 빠르고 가볍게, 그리고 파트별로 분리해서 사용할 수 있도록 만든 로깅 DLL입니다.

권장 사용 방식 1: 앱 수명 전체에서 쓰는 전역 로거

```csharp
public static class AppLog
{
    public static SPLogger Core { get; private set; } = null!;

    public static void Initialize()
    {
        Core = SPLogFactory.Create(options =>
        {
            options.Name = "Core";
            options.EnableFile = true;
            options.FilePath = "logs";
        });
    }

    public static void Shutdown()
    {
        Core.Dispose();
    }
}
```

이 방식은 애플리케이션 전체에서 하나의 로거를 오래 들고 쓰고 싶을 때 맞습니다.

권장 사용 방식 2: 짧은 범위에서만 쓰는 로거

```csharp
using var coreLog = SPLogFactory.Create(options =>
{
    options.Name = "Core";
    options.EnableConsole = true;
    options.EnableFile = true;
    options.FilePath = "logs";
});
```

왜 `using var`를 권장하냐면:

- `SPLogger`는 백그라운드 리소스를 잡고 있습니다.
- 로깅이 끝나면 반드시 `Dispose()`를 호출해야 합니다.
- 가장 쉬운 방법이 `using var`이고, 스코프가 끝날 때 자동으로 `Dispose()`가 호출됩니다.
- `Dispose()`를 하지 않으면 큐에 남아 있던 로그가 파일로 끝까지 flush되지 못할 수 있습니다.

언제 `Dispose()` 해야 하냐면:

- 그 로거를 더 이상 쓰지 않을 때 바로 해야 합니다.
- 보통 프로그램 종료 직전, 작업 단위가 끝나는 시점, 또는 객체의 수명이 끝나는 시점에 합니다.
- 애플리케이션 전체에서 오래 쓰는 로거라면 프로그램 종료 시점에 한 번 정리하면 됩니다.
- 잠깐만 쓰는 로거라면 사용 범위를 감싼 `using var`가 가장 안전합니다.

예:

```csharp
void Run()
{
    using var logger = SPLogFactory.Create(options =>
    {
        options.Name = "Core";
        options.EnableFile = true;
        options.FilePath = "logs";
    });

    logger.Information($"start");
    logger.Information($"end");
} // 여기서 자동 Dispose
```

이런 경우에는 아직 쓰는 중이므로 `Dispose()` 하면 안 됩니다.

```csharp
var logger = SPLogFactory.Create();
logger.Information($"start");
logger.Dispose();
logger.Information($"this should not be written");
```

외부 설정 파일을 쓰는 권장 방식:

```csharp
using var coreLog = SPLogFactory.CreateFromJsonFile("config/splog.core.json");
```

## Exception 전용 로깅

```csharp
try
{
    ProcessRequest();
}
catch (Exception ex)
{
    coreLog.Error(ex, "request failed");
}
```

왜 예외 전용 로깅이 필요하냐면:

- `logger.Error($"failed: {ex}")`와 `logger.Error(ex, "failed")`는 같은 게 아닙니다.
- 개발자마다 예외를 문자열에 직접 넣기 시작하면 로그 형식이 제각각이 됩니다.
- 예외 전용 오버로드를 쓰면 프로젝트 전체에서 일정한 형식으로 예외 로그를 남길 수 있습니다.

예외 로그에 남는 정보:

- 사용자가 남긴 메시지
- 예외 타입
- 예외 메시지
- 스택 트레이스
- `InnerException` 체인

예외 로그가 기록되는 위치:

- 콘솔 로깅이 켜져 있으면 같은 콘솔 출력으로 기록됩니다.
- 파일 로깅이 켜져 있으면 같은 로그 파일로 기록됩니다.
- 즉, 예외 로그는 일반 로그와 같은 출력 대상으로 나갑니다.

권장 예외 메서드:

- `logger.Warning(ex, "...")`
- `logger.Error(ex, "...")`
- `logger.Critical(ex, "...")`

## 로깅 문자열 사용 예시

기본 문자열:

```csharp
logger.Information("application started");
```

문자열 변수:

```csharp
var message = "network connected";
logger.Information(message);
```

보간 문자열:

```csharp
var userId = 1201;
logger.Information($"user connected: {userId}");
```

값 여러 개 넣기:

```csharp
var ip = "10.0.0.1";
var port = 443;
logger.Information($"connected to {ip}:{port}");
```

경고와 오류 문자열:

```csharp
logger.Warning("response delay detected");
logger.Error("request failed");
```

예외 로깅:

```csharp
try
{
    RunProcess();
}
catch (Exception ex)
{
    logger.Error(ex, "process failed");
}
```

## 외부 설정 파일

```csharp
using var logger = SPLogFactory.CreateFromJsonFile("docs/splog.sample.json");
```

현재 옵션을 JSON 파일로 저장할 수도 있습니다.

```csharp
var options = new SPLogOptions
{
    Name = "Core",
    EnableFile = true,
    FilePath = "logs"
};

SPLogConfiguration.SaveToJsonFile(options, "config/splog.core.json");
```

메모리 안의 기존 옵션 객체를 갱신하는 목적이라면 `Save`보다 `Update`를 쓰는 편이 맞습니다.

```csharp
SPLogConfiguration.Update(options);
SPLogConfiguration.UpdateFromJsonFile(options, "config/splog.core.json");
```

중요한 차이:

- `SPLogFactory.Create(options => { ... })` 안의 `options`는 람다 안에서만 쓰는 임시 객체입니다.
- 로거를 만든 뒤에는 그 객체를 직접 들고 있지 않습니다. 직접 `SPLogOptions` 변수를 만들어 둔 경우만 예외입니다.
- `Update(...)`, `UpdateFromJsonFile(...)`는 이미 내가 변수로 들고 있는 `SPLogOptions` 객체가 있을 때 쓰는 API입니다.

예:

```csharp
var options = new SPLogOptions();
SPLogConfiguration.UpdateFromJsonFile(options, "config/splog.core.json");

using var logger = SPLogFactory.Create(options);
```

저장할 때는 SPLog가 먼저 옵션 값을 정리하고 검증한 뒤 JSON으로 저장합니다.
그리고 그렇게 정리된 값이 메모리 안의 원래 `SPLogOptions` 객체에도 다시 반영됩니다.
즉, 저장 후에는 지금 들고 있는 `options` 객체도 저장된 값 기준으로 갱신됩니다.

## 파일 경로 규칙

- `FilePath`에 `logs`처럼 폴더만 주면 SPLog가 자동으로 `<Name>.log` 파일을 그 안에 만듭니다.
- 상대 경로는 exe 폴더인 `AppContext.BaseDirectory` 기준으로 해석됩니다.
- `D:\Logs\custom.log` 같은 절대 경로를 주면 그 위치와 파일명을 그대로 사용합니다.

예시:

- `FilePath = "logs"`, `Name = "Core"` -> `logs/Core_20260313.log`
- `FilePath = "logs"`, `Name = "Network"` -> `logs/Network_20260313.log`
- `FilePath = @"D:\Logs\custom.log"` -> `D:\Logs\custom_20260313.log`

## Rolling 동작

- `Daily`: `Core_20260313.log`
- `Hourly`: `Core_20260313_14.log`
- 시간 분리 후 크기 rolling까지 붙으면: `Core_20260313_14_001.log`

## SPLogOptions

| 옵션 | 기본값 | 선택지 | 자세한 설명 |
|---|---:|---|---|
| `Name` | `SPLog` | 비어 있지 않은 문자열 | 로그 한 줄에 기록될 로거 이름입니다. `FilePath`가 폴더일 때는 이 값이 자동 파일명에도 사용됩니다. 예를 들어 `Name = "Core"`이고 `FilePath = "logs"`이면 기본 파일은 `logs/Core.log`로 시작합니다. |
| `MinimumLevel` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` | 어떤 레벨부터 로그를 남길지 정합니다. `Trace`는 거의 모든 로그를 남기고, `Debug`는 개발용, `Information`은 일반적인 운영 기본값, `Warning`은 이상 징후 위주, `Error`는 실패 위주, `Critical`은 치명적 오류만 남깁니다. `None`은 로그를 모두 끕니다. |
| `UseUtcTimestamp` | `false` | `true`, `false` | `false`면 로컬 시간으로 기록해서 한 대의 PC에서 보기 쉽고, `true`면 UTC로 기록해서 여러 서버나 여러 지역 시스템을 함께 볼 때 시간 비교가 편합니다. |
| `IncludeThreadId` | `true` | `true`, `false` | 로그를 남긴 스레드 번호를 같이 기록합니다. 동시에 여러 작업이 도는 프로그램이면 `true`가 유용하고, 아주 단순한 프로그램이면 `false`로 줄여도 됩니다. |
| `IncludeLoggerName` | `true` | `true`, `false` | 로그 한 줄에 로거 이름을 같이 찍습니다. 여러 로거를 나눠 쓰면 보통 `true`가 좋습니다. 이미 파일이 완전히 분리되어 있고 로그 줄을 더 짧게 보고 싶을 때만 `false`를 고려할 수 있습니다. |
| `EnableConsole` | `true` | `true`, `false` | 콘솔 창에도 로그를 출력할지 정합니다. 개발 중, 테스트 중, 콘솔 앱에서는 유용합니다. 서비스나 백그라운드 앱에서는 파일만 쓰는 경우가 많습니다. |
| `EnableFile` | `false` | `true`, `false` | 파일로 로그를 남길지 정합니다. 실제 프로그램에서는 대부분 `true`로 사용합니다. `EnableConsole`과 `EnableFile`이 둘 다 `false`면 기록할 곳이 없으므로 로거 생성이 실패합니다. |
| `FilePath` | `logs` | 폴더 경로 또는 전체 파일 경로 | 기본 로그 위치입니다. `logs`처럼 폴더 경로만 주면 SPLog가 `<Name>.log`를 자동 생성합니다. `D:\Logs\custom.log`처럼 파일명까지 주면 그 이름을 그대로 사용합니다. 상대 경로는 exe 폴더 기준입니다. |
| `FileRollingMode` | `Daily` | `None`, `Daily`, `Hourly` | 시간 기준 파일 분리 방식입니다. `None`은 날짜나 시간 suffix 없이 한 이름으로만 갑니다. `Daily`는 `yyyyMMdd` 기준으로 하루마다 나눕니다. `Hourly`는 `yyyyMMdd_HH` 기준으로 한 시간마다 나눕니다. 로그 양이 많으면 `Hourly`가 관리하기 쉽습니다. |
| `MaxFileSizeBytes` | `10485760` | 0보다 큰 정수 | 파일 하나가 이 크기에 도달하면 다음 번호 파일로 넘어갑니다. 기본값은 10MB입니다. 값을 크게 하면 파일 수는 줄지만 파일 하나가 무거워지고, 작게 하면 파일 수는 늘지만 업로드나 확인은 쉬워집니다. |
| `MaxRollingFiles` | `14` | 0보다 큰 정수 | 최신 rolling 파일을 몇 개까지 보관할지 정합니다. 오래된 파일은 자동 삭제됩니다. 디스크 공간이 넉넉하면 늘리고, 디스크를 아껴야 하면 줄이면 됩니다. |
| `QueueCapacity` | `8192` | 0보다 큰 정수 | 메모리에 잠깐 쌓아둘 수 있는 로그 개수입니다. 값이 너무 작으면 로그 폭주 때 일부 로그가 버려질 수 있고, 너무 크면 메모리를 더 사용합니다. |
| `BatchSize` | `10` | 0보다 큰 정수 | 한 번에 묶어서 처리할 최대 로그 개수입니다. 기본값 `10`은 일반적인 사용에서 속도와 단순함의 균형이 괜찮은 값입니다. SPLog는 10개가 다 찰 때까지 무조건 기다리지 않고, 지금 들어와 있는 개수만큼 먼저 기록할 수 있습니다. 값을 키우면 보통 파일 쓰기 성능이 좋아집니다. |
| `FlushIntervalMs` | `100` | 0보다 큰 정수 | 백그라운드 쓰기 주기입니다. `100`이면 대략 0.1초마다 파일로 밀어냅니다. 값을 작게 하면 더 빨리 디스크에 반영되지만 I/O가 늘고, 크게 하면 성능은 좋아질 수 있지만 파일 반영은 조금 늦어집니다. |
| `FileBufferSize` | `65536` | `1024` 이상 정수 | 파일 쓰기 버퍼 크기입니다. 너무 작으면 자잘한 쓰기가 늘고, 어느 정도 크게 두면 성능에 도움이 됩니다. 기본값이면 보통 충분합니다. |
| `BlockWhenQueueFull` | `true` | `true`, `false` | 큐가 가득 찼을 때의 동작입니다. 기본값 `true`는 자리가 날 때까지 기다려서 로그 유실을 줄이는 쪽입니다. `false`로 바꾸면 프로그램은 더 계속 빨리 진행되지만, 로그가 몰릴 때 새 로그 일부를 버릴 수 있습니다. |

## 참고

- 종료 시 남은 로그를 flush하려면 각 로거를 반드시 `Dispose`해야 합니다.
- 그래서 `using var logger = ...` 패턴을 권장합니다.
- 의도한 공유 append가 아니라면 여러 로거를 같은 파일 경로에 보내지 않는 것이 좋습니다.
- 현재 구조는 로거 인스턴스당 백그라운드 worker 하나를 사용합니다.
## FileConflictMode

- `FileConflictMode` 옵션은 "같은 이름의 현재 대상 로그 파일이 이미 있을 때" 어떻게 처리할지 정합니다.
- `Append`: 기존 파일에 그대로 이어서 기록합니다.
- `CreateNew`: 새 파일을 구분되게 만듭니다.

예시:

- `Name = "Core"`, `FilePath = "logs"`, `FileRollingMode = Daily`, `FileConflictMode = Append`
  -> 첫 실행: `Core_20260313.log`
  -> 같은 날 다시 실행: 계속 `Core_20260313.log`
- `Name = "Core"`, `FilePath = "logs"`, `FileRollingMode = Daily`, `FileConflictMode = CreateNew`
  -> 첫 실행: `Core_20260313.log`
  -> 같은 날 다시 실행: `Core_20260313_001.log`
  -> 그 다음 실행: `Core_20260313_002.log`
- `Name = "Core"`, `FilePath = "logs"`, `FileRollingMode = Hourly`, `FileConflictMode = CreateNew`
  -> 해당 시간 첫 실행: `Core_20260313_14.log`
  -> 같은 시간 다시 실행: `Core_20260313_14_001.log`
- 용량 롤링이 `CreateNew`와 겹치면 번호를 계속 이어서 씁니다.
  -> 예: 시작 파일이 `Core_20260313_001.log`이고 이 파일이 `MaxFileSizeBytes`를 넘기면 다음 파일은 `Core_20260313_002.log`, 그 다음은 `Core_20260313_003.log`
- 용량 롤링이 `Append`와 겹치면 먼저 현재 파일에 이어서 쓰다가, 용량 제한을 넘는 시점에 다음 번호 파일로 넘어갑니다.

정리:

- 첫 파일은 항상 기본 이름으로 시작합니다.
- `CreateNew`는 같은 기간에 같은 이름 파일이 이미 있을 때만 `_001`, `_002` 같은 번호를 붙입니다.
