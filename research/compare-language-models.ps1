[CmdletBinding()]
param(
    [string[]] $Models = @(
        'deepseek-v4-flash',
        'deepseek-v4-pro',
        'kimi-k2.6',
        'kimi-k3'
    )
)

$ErrorActionPreference = 'Stop'

$evaluationPrompt = @'
You are a careful English speech coach. For each spoken transcript below, return
two versions:

- corrected: minimal grammar correction that preserves the exact meaning,
  uncertainty, names, dates, and numbers.
- polished: natural, confident spoken English with better vocabulary, while
  still preserving meaning and not adding facts.

Also return a short note array naming only meaningful changes. Do not make the
speaker more certain than they were.

Output one JSON object with a "results" array. Each item must contain an integer
"id", strings "corrected" and "polished", and a string array "notes".

1. Yesterday I discuss with client and we decide to move deadline on Friday
   because team have not enough time.
2. So, um, what I wanted to say is the cache line padded long, it prevent false
   sharing when a few threads update counters.
3. There were three servers - actually no, four - and we migrated two on May 14.
   Do not change those numbers.
4. I do not think this proposal is not bad, but it needs less dependencies and
   it should start faster.
5. Could you sent me the files what we was talking about? I need it until end
   of day.
6. We deployed Nethermind version 1.31 to Glamsterdam devnet 7, and maybe the
   regression started after block 4200.
7. I have did it this way for avoid breaking current behavior.
8. Basically we can perhaps remove this check, but I am not fully sure because
   the nullable caller may still pass null.
'@

function Invoke-ModelEvaluation {
    param(
        [Parameter(Mandatory)]
        [string] $Model
    )

    $isKimi = $Model.StartsWith('kimi-', [StringComparison]::OrdinalIgnoreCase)
    $apiKey = if ($isKimi) {
        [Environment]::GetEnvironmentVariable('MOONSHOT_API_KEY')
    }
    else {
        [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY')
    }

    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw "The API key required for $Model is not configured."
    }

    $uri = if ($isKimi) {
        'https://api.moonshot.ai/v1/chat/completions'
    }
    else {
        'https://api.deepseek.com/chat/completions'
    }

    $request = @{
        model = $Model
        messages = @(
            @{
                role = 'system'
                content = 'Follow the requested schema exactly and return JSON only.'
            }
            @{
                role = 'user'
                content = $evaluationPrompt
            }
        )
        response_format = @{
            type = 'json_object'
        }
    }

    switch ($Model) {
        'kimi-k2.6' {
            $request.thinking = @{ type = 'disabled' }
            $request.max_tokens = 2000
        }
        'kimi-k3' {
            $request.reasoning_effort = 'low'
            $request.max_completion_tokens = 2000
        }
        default {
            $request.thinking = @{ type = 'disabled' }
            $request.temperature = 0.2
            $request.max_tokens = 3000
        }
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod `
        -Uri $uri `
        -Headers @{ Authorization = "Bearer $apiKey" } `
        -ContentType 'application/json' `
        -Method Post `
        -Body ($request | ConvertTo-Json -Depth 12)
    $stopwatch.Stop()

    $content = $response.choices[0].message.content
    $parsedOutput = $null
    $parseError = $null
    if (-not [string]::IsNullOrWhiteSpace($content)) {
        try {
            $parsedOutput = $content | ConvertFrom-Json
        }
        catch {
            $parseError = $_.Exception.Message
        }
    }

    [pscustomobject]@{
        model = $Model
        elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        promptTokens = $response.usage.prompt_tokens
        completionTokens = $response.usage.completion_tokens
        finishReason = $response.choices[0].finish_reason
        output = $parsedOutput
        outputRaw = if ($parsedOutput) { $null } else { $content }
        parseError = $parseError
    }
}

$results = foreach ($model in $Models) {
    try {
        Invoke-ModelEvaluation -Model $model
    }
    catch {
        [pscustomobject]@{
            model = $model
            error = $_.Exception.Message -replace 'org-[^ ]+', 'organization'
        }
    }
}

$results | ConvertTo-Json -Depth 20
