# Validar los Excel de datos reales

Desde la raíz del repositorio, con Python y `openpyxl` (verificado con 3.1.5):

```powershell
python -B scripts/validate_real_data.py "../Datos"
```

La herramienta lee los libros locales y emite un informe JSON agregado. No abre Excel, no consulta fuentes remotas, no escribe en los libros y no importa nada al juego.

## Cómo interpretar el informe

Los recuentos de personas se hacen por ID, agrupando los duplicados de los libros de jugadores. Se compara `Equipo Actual` con `Nombre` del club después de normalizar Unicode, mayúsculas y espacios. No se cruzan países, temporadas ni competiciones.

| Resultado | Significado |
| --- | --- |
| `one_club_candidate` | El nombre coincide con un solo ID de club. Es un candidato, no un vínculo confirmado. |
| `ambiguous_club` | El nombre coincide con más de un ID de club. |
| `unmatched_club` | El nombre no aparece en el catálogo de clubes. |
| `no_club` | No hay nombre de equipo utilizable. |
| `conflicting_club_labels` | Un mismo ID tiene varios nombres de equipo distintos. |

El código de salida `0` indica que el informe se generó, no que el catálogo esté listo para importar. Si falta un libro obligatorio o cambia el esquema de columnas, el comando devuelve `1` sin producir un informe parcial. Los Excel y el eventual catálogo real se mantienen fuera del repositorio.

## Pruebas con libros sintéticos

```powershell
python -B -m unittest discover -s tests/data_validation -v
```
