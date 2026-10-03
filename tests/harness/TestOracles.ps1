# DiskMaster Pro WinUI 3 — Test Oracles and Spec Helpers
# Author: Test Writer Agent (E2E Track)

class NvmeSpecOracle {
    # Synthesizes an authoritative NVMe Base Spec 5.14.1.2 compliant Log Page 0x02 buffer (512 bytes)
    static [byte[]] CreateSampleNvmeLogPage([ulong]$powerOnHours, [ulong]$powerCycles, [ulong]$mediaErrors, [ushort]$tempKelvin, [byte]$spare, [byte]$wear, [ulong]$readUnits, [ulong]$writeUnits) {
        $buf = [byte[]]::new(512)
        
        # Byte 0: Critical Warning
        $buf[0] = 0
        
        # Byte 1-2: Composite Temperature (Kelvin)
        $tBytes = [System.BitConverter]::GetBytes($tempKelvin)
        $buf[1] = $tBytes[0]
        $buf[2] = $tBytes[1]
        
        # Byte 3: Available Spare
        $buf[3] = $spare
        # Byte 4: Available Spare Threshold
        $buf[4] = 10
        # Byte 5: Percentage Used (Wear)
        $buf[5] = $wear
        
        # Offset 32: Data Units Read (16 bytes)
        $rBytes = [System.BitConverter]::GetBytes($readUnits)
        [System.Array]::Copy($rBytes, 0, $buf, 32, $rBytes.Length)
        
        # Offset 48: Data Units Written (16 bytes)
        $wBytes = [System.BitConverter]::GetBytes($writeUnits)
        [System.Array]::Copy($wBytes, 0, $buf, 48, $wBytes.Length)
        
        # Offset 96: Controller Busy Time (16 bytes) - formerly misused as PowerOnHours!
        $busyBytes = [System.BitConverter]::GetBytes([ulong]999999)
        [System.Array]::Copy($busyBytes, 0, $buf, 96, $busyBytes.Length)
        
        # Offset 112: Power Cycles (16 bytes)
        $pcBytes = [System.BitConverter]::GetBytes($powerCycles)
        [System.Array]::Copy($pcBytes, 0, $buf, 112, $pcBytes.Length)
        
        # Offset 128: Power On Hours (16 bytes per NVMe Base Spec Section 5.14.1.2)
        $pohBytes = [System.BitConverter]::GetBytes($powerOnHours)
        [System.Array]::Copy($pohBytes, 0, $buf, 128, $pohBytes.Length)
        
        # Offset 144: Unsafe Shutdowns (16 bytes)
        $usBytes = [System.BitConverter]::GetBytes([ulong]12)
        [System.Array]::Copy($usBytes, 0, $buf, 144, $usBytes.Length)
        
        # Offset 160: Media Errors (16 bytes)
        $meBytes = [System.BitConverter]::GetBytes($mediaErrors)
        [System.Array]::Copy($meBytes, 0, $buf, 160, $meBytes.Length)
        
        # Offset 176: Error Log Entries (16 bytes)
        $errBytes = [System.BitConverter]::GetBytes([ulong]0)
        [System.Array]::Copy($errBytes, 0, $buf, 176, $errBytes.Length)
        
        # Offset 200..215: Thermal sensors 1..8 (2 bytes each)
        for ($s = 0; $s -lt 8; $s++) {
            $sBytes = [System.BitConverter]::GetBytes([ushort]($tempKelvin + $s))
            [System.Array]::Copy($sBytes, 0, $buf, 200 + ($s * 2), 2)
        }
        
        return $buf
    }

    # Authoritative reference extraction per specification
    static [ulong] ExtractPowerOnHours([byte[]]$buffer, [int]$logOffset) {
        return [System.BitConverter]::ToUInt64($buffer, $logOffset + 128)
    }

    static [ulong] ExtractPowerCycles([byte[]]$buffer, [int]$logOffset) {
        return [System.BitConverter]::ToUInt64($buffer, $logOffset + 112)
    }

    static [ulong] ExtractMediaErrors([byte[]]$buffer, [int]$logOffset) {
        return [System.BitConverter]::ToUInt64($buffer, $logOffset + 160)
    }
}

class PipeStressOracle {
    # Generates a string of exact byte length for pipe buffer testing
    static [string] GenerateLargePayload([int]$byteCount, [char]$fillChar = 'X') {
        return [string]::new($fillChar, $byteCount)
    }
}
