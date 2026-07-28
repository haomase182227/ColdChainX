import xml.etree.ElementTree as ET

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()

diagrams = root.findall('diagram')
for d in diagrams:
    if d.attrib.get('name') == '0 - Overall Conceptual Data Model':
        model = d.find('mxGraphModel')
        if model is not None:
            root_el = model.find('root')
            if root_el is not None:
                cells_to_remove = []
                for cell in root_el.findall('mxCell'):
                    cid = cell.attrib.get('id', '')
                    if cid.startswith('area_'):
                        cells_to_remove.append(cell)
                for cell in cells_to_remove:
                    root_el.remove(cell)
                print(f"Removed {len(cells_to_remove)} dashed area containers.")

tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Updated docs/Conceptual-Data-Model.drawio successfully.")
