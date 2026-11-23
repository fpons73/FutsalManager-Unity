import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile

from scripts import validate_real_data


CLUBS = "Enciclopedia_Futsal_Equipas_Masculino1.xlsx"
COACHES = "Enciclopedia_Futsal_Entrenadores_Masculino1.xlsx"
PLAYERS = "Enciclopedia_Futsal_Jogadores_Activos_Masculino{}.xlsx"


def workbook(path, sheet, rows):
    namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
    relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    content = []
    for row in rows:
        cells = "".join(f'<c t="inlineStr"><is><t xml:space="preserve">{escape(str(item))}</t></is></c>'
                        for item in row)
        content.append(f"<row>{cells}</row>")
    with ZipFile(path, "w") as result:
        result.writestr("[Content_Types].xml",
                        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                        '</Types>')
        result.writestr("xl/workbook.xml",
                        f'<workbook xmlns="{namespace}" xmlns:r="{relationship}"><sheets>'
                        f'<sheet name="{sheet}" sheetId="1" r:id="rId1"/></sheets></workbook>')
        result.writestr("xl/_rels/workbook.xml.rels",
                        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                        f'<Relationship Id="rId1" Type="{relationship}/worksheet" Target="/xl/worksheets/sheet1.xml"/>'
                        '</Relationships>')
        result.writestr("xl/worksheets/sheet1.xml",
                        f'<worksheet xmlns="{namespace}"><sheetData>{"".join(content)}</sheetData></worksheet>')


def run(folder):
    output, errors = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
        status = validate_real_data.main([str(folder)])
    return status, output.getvalue(), errors.getvalue()


class ValidateRealDataTests(unittest.TestCase):
    def test_reports_candidate_categories_per_id_without_changing_inputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            workbook(folder / CLUBS, "Equipas",
                     [("ID", "Nombre"), (10, "Atlético Norte"), (20, "Same Club"), (30, "Same Club")])
            workbook(folder / COACHES, "Treinadores",
                     [("ID", "Equipo Actual"), (1, "atlético norte"), (2, ""), (3, "Same Club")])
            workbook(folder / PLAYERS.format(1), "Jogadores",
                     [("ID", "Equipo Actual", "Nombre"), (1, " ATLÉTICO  NORTE ", "Hidden Player"),
                      (2, "Same Club", "Hidden Player"), (3, "Not in catalog", "Hidden Player"),
                      (4, "", "Hidden Player"), (5, "Same Club", "Hidden Player"),
                      ("", "Atlético Norte", "Hidden Player"), ("not-id", "", "Hidden Player")])
            workbook(folder / PLAYERS.format(2), "Jogadores",
                     [("ID", "Equipo Actual"), (1, "Atlético Norte"), (5, "Atlético Norte")])
            before = {f.name: f.read_bytes() for f in folder.iterdir()}

            status, output, errors = run(folder)

            self.assertEqual(0, status)
            self.assertEqual("", errors)
            report = json.loads(output)
            self.assertEqual("unifutsal.real-data-validation/v1", report["schema"])
            self.assertEqual({"rows": 9, "unique_ids": 5, "duplicate_id_rows": 2,
                              "missing_id_rows": 1, "invalid_id_rows": 1}, report["players"]["records"])
            self.assertEqual({"one_club_candidate": 1, "ambiguous_club": 1, "unmatched_club": 1,
                              "no_club": 1, "conflicting_club_labels": 1}, report["players"]["memberships"])
            self.assertEqual({"one_club_candidate": 1, "ambiguous_club": 1, "unmatched_club": 0,
                              "no_club": 1, "conflicting_club_labels": 0}, report["coaches"]["memberships"])
            self.assertEqual(3, report["clubs"]["records"]["unique_ids"])
            self.assertEqual(1, report["clubs"]["ambiguous_name_keys"])
            for value in ["Hidden Player", "Atlético Norte", "Not in catalog", "Same Club"]:
                self.assertNotIn(value, output)
            self.assertEqual(before, {f.name: f.read_bytes() for f in folder.iterdir()})

    def test_rejects_missing_catalog_without_producing_partial_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            status, output, errors = run(Path(tmp))
        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("Faltan los Excel", errors)

    def test_rejects_a_workbook_without_the_required_columns(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            workbook(folder / CLUBS, "Equipas", [("ID", "unexpected"), (1, "Hidden Club")])
            workbook(folder / COACHES, "Treinadores", [("ID", "Equipo Actual")])
            workbook(folder / PLAYERS.format(1), "Jogadores", [("ID", "Equipo Actual")])

            status, output, errors = run(folder)

        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("columnas requeridas", errors)
        self.assertNotIn("Hidden Club", errors)


if __name__ == "__main__":
    unittest.main()
