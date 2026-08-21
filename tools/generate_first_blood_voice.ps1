param(
    [string]$OutputPath = "$PSScriptRoot\audio\first-blood-announcer.wav"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Speech

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voice = $synthesizer.GetInstalledVoices() |
        Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq "en-US" -and $_.VoiceInfo.Gender -eq "Male" } |
        Select-Object -First 1
    if ($null -eq $voice) {
        throw "An enabled en-US male System.Speech voice is required."
    }

    $synthesizer.SelectVoice($voice.VoiceInfo.Name)
    $synthesizer.Rate = -2
    $synthesizer.Volume = 100
    $synthesizer.SetOutputToWaveFile($OutputPath)
    $synthesizer.Speak("First blood!")
    $synthesizer.SetOutputToNull()

    Write-Host "Generated First Blood announcer with $($voice.VoiceInfo.Name): $OutputPath"
}
finally {
    $synthesizer.Dispose()
}
