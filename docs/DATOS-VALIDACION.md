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

## Registrar decisiones manuales

Tras verificar el vínculo con una fuente fechada, guarda un CSV UTF-8 **fuera del repositorio**, por ejemplo en `../Datos/revision-local.csv`. Usa una fila por persona, con su ID, el texto de su equipo en el Excel y el ID del club corroborado. Estos valores son ficticios:

```csv
kind,person_id,team_label,club_id
player,101,Club de ejemplo,20
coach,204,Otra etiqueta de ejemplo,30
```

```powershell
python -B scripts/validate_real_data.py "../Datos" --reviewed-links "../Datos/revision-local.csv"
```

El informe cuenta estas decisiones en `manually_reviewed_club`, sin mostrar IDs ni etiquetas y sin cambiarlas en el Excel. Cada decisión afecta solo a ese jugador o entrenador. Si el equipo ya tiene candidatos por nombre, el club elegido debe estar entre ellos. Las personas sin equipo o con etiquetas contradictorias quedan pendientes. Una fila inválida, duplicada o desactualizada detiene el informe completo; corrige la línea indicada y vuelve a ejecutarlo.

La herramienta comprueba que la decisión concuerda con los libros; no verifica la fuente externa, la vigencia del vínculo ni los permisos para publicar los datos.

## Consultar los clubes candidatos de una persona

```powershell
python -B scripts/validate_real_data.py "../Datos" --lookup player 101
```

Usa `player` o `coach` y el ID del Excel correspondiente; `101` es un ejemplo. La consulta muestra las etiquetas del equipo normalizadas y, por cada coincidencia, el ID del club, su nombre y el país y la ciudad si están informados. El catálogo debe incluir las columnas `País` y `Ciudad`, aunque sus celdas puedan estar vacías. Si hay varios candidatos o etiquetas contradictorias, los muestra por separado sin resolver el vínculo.

El resultado es para revisión local: no incluye el nombre ni la fecha de nacimiento de la persona, pero sí el ID consultado. No publiques la salida. Una vez corroborado el vínculo, puedes copiar el ID del club y la etiqueta al CSV manual. `--lookup` no se combina con `--reviewed-links` ni escribe en los Excel.

## Pruebas con libros sintéticos

```powershell
python -B -m unittest discover -s tests/data_validation -v
```
