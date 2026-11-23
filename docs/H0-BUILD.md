# UniFutsal Manager - Entorno fijado y guia de build (H0)

## Versiones verificadas en Windows 11 x64
- Sistema operativo: Windows 11 x64
- Git: 2.50.1.windows.1
- .NET SDK: 10.0.400 (Domain / Application / Infrastructure en netstandard2.1, Headless / Tests en net10.0)
- SQLite: Microsoft.Data.Sqlite 10.0.13 + e_sqlite3.dll nativo win-x64
- Unity: Unity 6.3 LTS (6000.3.28f1), plantilla Universal 2D + UI Toolkit

## Pasos para repetir el build desde cero
1. Ejecutar pruebas y verificacion sin Unity:
   dotnet test
   dotnet run --project src/UniFutsal.Headless
2. Compilar y sincronizar DLLs + SQLite nativo hacia Unity:
   .\scripts\sync-unity-plugins.ps1
3. Abrir desktop/UniFutsal.Unity en Unity 6000.3.28f1.
4. Ir a File -> Build Profiles -> Windows y pulsar Build eligiendo la carpeta builds/H0-Windows.
5. Ejecutar builds/H0-Windows/UniFutsal Manager.exe sin conexion a red y verificar el mensaje "Datos locales disponibles".
