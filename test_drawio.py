import xml.etree.ElementTree as ET
import uuid
import html

def gen_drawio():
    mxfile = ET.Element('mxfile', host="Electron", compressed="false", version="28.2.8")
    diagram = ET.SubElement(mxfile, 'diagram', id=str(uuid.uuid4()), name="Test Diagram")
    mxGraphModel = ET.SubElement(diagram, 'mxGraphModel', dx="1000", dy="1000", grid="1", gridSize="10", guides="1", tooltips="1", connect="1", arrows="1", fold="1", page="1", pageScale="1", pageWidth="827", pageHeight="1169", background="#ffffff", math="0", shadow="0")
    root = ET.SubElement(mxGraphModel, 'root')
    ET.SubElement(root, 'mxCell', id="0")
    ET.SubElement(root, 'mxCell', id="1", parent="0")

    # Title
    ET.SubElement(root, 'mxCell', id="title", value="Test Diagram", style="text;html=1;align=center;verticalAlign=middle;whiteSpace=wrap;fontSize=24;fontStyle=1;", parent="1", vertex="1").append(
        ET.Element('mxGeometry', y="30", width="827", height="60", **{"as": "geometry"})
    )

    # Actor 1
    actor_id = "actor_1"
    ET.SubElement(root, 'mxCell', id=actor_id, value="Client", style="shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;", parent="1", vertex="1").append(
        ET.Element('mxGeometry', x="100", y="100", width="30", height="60", **{"as": "geometry"})
    )
    
    # Actor 1 Lifeline
    line1_id = "line_1"
    end1_id = "end_1"
    ET.SubElement(root, 'mxCell', id=end1_id, value="", style="opacity=0;fillOpacity=0;strokeOpacity=0;", parent="1", vertex="1", connectable="0").append(
        ET.Element('mxGeometry', x="115", y="400", width="1", height="1", **{"as": "geometry"})
    )
    ET.SubElement(root, 'mxCell', id=line1_id, value="", style="edgeStyle=none;html=1;dashed=1;endArrow=none;startArrow=none;", parent="1", source=actor_id, target=end1_id, edge="1").append(
        ET.Element('mxGeometry', relative="1", **{"as": "geometry"})
    )

    # Controller
    ctrl_id = "ctrl_1"
    ET.SubElement(root, 'mxCell', id=ctrl_id, value="Controller", style="rounded=0;whiteSpace=wrap;html=1;", parent="1", vertex="1").append(
        ET.Element('mxGeometry', x="300", y="120", width="100", height="40", **{"as": "geometry"})
    )
    
    # Controller Lifeline
    line2_id = "line_2"
    end2_id = "end_2"
    ET.SubElement(root, 'mxCell', id=end2_id, value="", style="opacity=0;fillOpacity=0;strokeOpacity=0;", parent="1", vertex="1", connectable="0").append(
        ET.Element('mxGeometry', x="350", y="400", width="1", height="1", **{"as": "geometry"})
    )
    ET.SubElement(root, 'mxCell', id=line2_id, value="", style="edgeStyle=none;html=1;dashed=1;endArrow=none;startArrow=none;", parent="1", source=ctrl_id, target=end2_id, edge="1").append(
        ET.Element('mxGeometry', relative="1", **{"as": "geometry"})
    )

    # Message
    msg_sp = "msg_sp"
    msg_tp = "msg_tp"
    ET.SubElement(root, 'mxCell', id=msg_sp, value="", style="opacity=0;", parent="1", vertex="1").append(
        ET.Element('mxGeometry', x="115", y="200", width="1", height="1", **{"as": "geometry"})
    )
    ET.SubElement(root, 'mxCell', id=msg_tp, value="", style="opacity=0;", parent="1", vertex="1").append(
        ET.Element('mxGeometry', x="350", y="200", width="1", height="1", **{"as": "geometry"})
    )
    
    msg_edge = "msg_edge"
    ET.SubElement(root, 'mxCell', id=msg_edge, value="", style="edgeStyle=none;html=1;endArrow=block;endFill=1;", parent="1", source=msg_sp, target=msg_tp, edge="1").append(
        ET.Element('mxGeometry', relative="1", **{"as": "geometry"})
    )
    
    # Message Label
    ET.SubElement(root, 'mxCell', id="msg_lbl", value="1. POST /api", style="text;html=1;align=center;verticalAlign=bottom;", parent=msg_edge, vertex="1", connectable="0").append(
        ET.Element('mxGeometry', relative="1", y="-5", **{"as": "geometry"})
    )

    with open('test.drawio', 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="UTF-8"?>\n')
        f.write(ET.tostring(mxfile, encoding="unicode"))

gen_drawio()
