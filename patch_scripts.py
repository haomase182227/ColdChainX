import os
import re

new_generate_xml = """    def generate_xml(self):
        xml = f'''  <diagram name="{self.title}" id="{self.title.replace(' ', '_')}">
    <mxGraphModel dx="1000" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{math.ceil(self.page_width)}" pageHeight="{math.ceil(self.page_height)}" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />
        <mxCell id="title_{self.title.replace(' ', '_').replace('/', '_')}" value="{self.title}" style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=20;fontStyle=1;fontColor=#000000;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="20" y="20" width="500" height="30" as="geometry" />
        </mxCell>
'''
        xs = {}
        for i, p in enumerate(self.participants):
            cx = 100 + i * self.spacing_x
            xs[p] = cx
            is_actor = (i == 0)
            
            if is_actor:
                style = "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;fillColor=#ffffff;strokeColor=#000000;fontColor=#000000;strokeWidth=1;fontSize=14;"
                xml += f'''        <mxCell id="p_{p.replace(' ', '_')}" value="{p}" style="{style}" parent="1" vertex="1">
          <mxGeometry x="{cx - 20}" y="{self.start_y - 60}" width="40" height="80" as="geometry" />
        </mxCell>\\n'''
            else:
                style = "rounded=0;whiteSpace=wrap;html=1;align=center;verticalAlign=middle;fillColor=#ffffff;strokeColor=#000000;fontColor=#000000;strokeWidth=1;fontSize=14;fontStyle=1;"
                xml += f'''        <mxCell id="p_{p.replace(' ', '_')}" value="{p}" style="{style}" parent="1" vertex="1">
          <mxGeometry x="{cx - self.part_width/2}" y="{self.start_y - self.part_height}" width="{self.part_width}" height="{self.part_height}" as="geometry" />
        </mxCell>\\n'''
            
            xml += f'''        <mxCell id="lend_{p.replace(' ', '_')}" value="" style="opacity=0;fillOpacity=0;strokeOpacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{cx}" y="{self.page_height - 80}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="lline_{p.replace(' ', '_')}" value="" style="edgeStyle=none;html=1;dashed=1;dashPattern=8 8;endArrow=none;startArrow=none;strokeColor=#000000;strokeWidth=1;" parent="1" source="p_{p.replace(' ', '_')}" target="lend_{p.replace(' ', '_')}" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>\\n'''

        active = {p: [] for p in self.participants}
        self.segments = []
        self.act_counter = 0
        self.msg_counter = 0
        self.frame_counter = 0
        self.y = self.start_y
        self.msg_xml = ""
        self.act_xml = ""
        self.frame_xml = ""
        
        self.process_steps(self.steps, active, xs, 1)

        for p in self.participants:
            while active[p]:
                act = active[p].pop()
                self.segments.append({'p': p, 'start': act['start_y'], 'end': self.y})
                
        for i, seg in enumerate(self.segments):
            p = seg['p']
            start = seg['start']
            end = seg['end']
            self.act_xml += f'''        <mxCell id="act_{self.title.replace(' ', '')}_{i}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#ffffff;strokeColor=#000000;strokeWidth=1;" parent="1" vertex="1">
          <mxGeometry x="{xs[p] - self.act_width/2}" y="{start}" width="{self.act_width}" height="{end - start}" as="geometry" />
        </mxCell>\\n'''
                
        cap_y = self.page_height - 40
        xml += self.frame_xml + self.act_xml + self.msg_xml
        xml += f'''        <mxCell id="cap_{self.title.replace(' ', '_').replace('/', '_')}" value="Figure: {self.title}" style="text;html=1;align=center;verticalAlign=middle;whiteSpace=wrap;fontSize=14;fontStyle=2;fontColor=#000000;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="0" y="{cap_y}" width="{self.page_width}" height="30" as="geometry" />
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>'''
        return xml"""

new_process_steps = """    def process_steps(self, steps_list, active, xs, depth):
        for step in steps_list:
            if isinstance(step, tuple):
                self.y += self.spacing_y
                src, tgt, text, is_return = step
                
                if is_return and active[src]:
                    act = active[src].pop()
                    self.segments.append({'p': src, 'start': act['start_y'], 'end': self.y})
                
                if not is_return:
                    active[tgt].append({'start_y': self.y})
                
                msg_id = f"msg_{self.title.replace(' ', '')}_{self.msg_counter}"
                self.msg_counter += 1
                sp_x = xs[src]
                tp_x = xs[tgt]
                
                if sp_x < tp_x:
                    sp_x += self.act_width/2 if active[src] else 0
                    tp_x -= self.act_width/2 if active[tgt] else 0
                else:
                    sp_x -= self.act_width/2 if active[src] else 0
                    tp_x += self.act_width/2 if active[tgt] else 0

                style = "edgeStyle=orthogonalEdgeStyle;rounded=0;html=1;strokeColor=#000000;strokeWidth=1;"
                if is_return:
                    style += "dashed=1;dashPattern=4 4;endArrow=open;endFill=0;"
                else:
                    style += "endArrow=block;endFill=1;"
                    
                xml_text = text.replace("&", "&amp;")
                self.msg_xml += f'''        <mxCell id="{msg_id}_sp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{sp_x}" y="{self.y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_tp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{tp_x}" y="{self.y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_edge" value="" style="{style}" parent="1" source="{msg_id}_sp" target="{msg_id}_tp" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_lbl" value="{xml_text}" style="text;html=1;align=center;verticalAlign=bottom;fontSize=12;fontColor=#000000;labelBackgroundColor=none;" parent="{msg_id}_edge" vertex="1" connectable="0">
          <mxGeometry y="-10" x="0" relative="1" as="geometry" />
        </mxCell>\\n'''
            
            elif isinstance(step, dict):
                self.y += 20
                frame_start_y = self.y
                self.y += 10
                
                involved = set()
                def find_parts(sl):
                    for s in sl:
                        if isinstance(s, tuple):
                            involved.add(s[0])
                            involved.add(s[1])
                        elif isinstance(s, dict):
                            find_parts(s.get("steps_if", []))
                            find_parts(s.get("steps_else", []))
                
                find_parts(step.get("steps_if", []))
                find_parts(step.get("steps_else", []))
                
                if not involved:
                    involved = set(self.participants)
                    
                inv_xs = [xs[p] for p in involved]
                min_x = min(inv_xs) - 60
                max_x = max(inv_xs) + 60
                
                fid = f"frame_{self.title.replace(' ', '')}_{self.frame_counter}"
                self.frame_counter += 1
                
                import copy
                active_before = copy.deepcopy(active)
                
                self.process_steps(step.get("steps_if", []), active, xs, depth + 1)
                
                if "steps_else" in step:
                    active_after_if = copy.deepcopy(active)
                    self.y += 20
                    line_y = self.y
                    self.frame_xml += f'''        <mxCell id="{fid}_line" value="" style="edgeStyle=none;html=1;dashed=1;endArrow=none;strokeWidth=1;strokeColor=#000000;" parent="1" edge="1">
          <mxGeometry relative="1" as="geometry">
            <mxPoint x="{min_x}" y="{line_y}" as="sourcePoint" />
            <mxPoint x="{max_x}" y="{line_y}" as="targetPoint" />
          </mxGeometry>
        </mxCell>
        <mxCell id="{fid}_else_lbl" value="{step.get('else_condition', '')}" style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=11;fontColor=#000000;fontStyle=1;" parent="1" vertex="1">
          <mxGeometry x="{min_x + 5}" y="{line_y + 5}" width="200" height="20" as="geometry" />
        </mxCell>\\n'''
                    
                    for p in self.participants:
                        active[p] = []
                        for i in range(len(active_before[p])):
                            if i >= len(active_after_if[p]):
                                active[p].append({'start_y': self.y})
                            else:
                                active[p].append({'start_y': active_before[p][i]['start_y']})
                                
                    self.process_steps(step["steps_else"], active, xs, depth + 1)
                
                self.y += 20
                frame_height = self.y - frame_start_y
                
                self.frame_xml += f'''        <mxCell id="{fid}" value="{step.get('type', 'alt')}" style="shape=umlFrame;whiteSpace=wrap;html=1;pointerEvents=0;width=50;height=20;fillColor=none;strokeColor=#000000;strokeWidth=1;fontSize=12;fontStyle=1;" parent="1" vertex="1">
          <mxGeometry x="{min_x}" y="{frame_start_y}" width="{max_x - min_x}" height="{frame_height}" as="geometry" />
        </mxCell>
        <mxCell id="{fid}_lbl" value="{step.get('condition', '')}" style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=11;fontColor=#000000;fontStyle=1;" parent="1" vertex="1">
          <mxGeometry x="{min_x + 55}" y="{frame_start_y}" width="200" height="20" as="geometry" />
        </mxCell>\\n'''
"""

files = [
    '/Users/macbuituananh/.gemini/antigravity-ide/brain/d15d88ee-b909-4d48-87d6-28c49394a4f9/scratch/generate_auth_sd_with_frames.py',
    '/Users/macbuituananh/.gemini/antigravity-ide/brain/d15d88ee-b909-4d48-87d6-28c49394a4f9/scratch/generate_dispatch_sd.py',
    '/Users/macbuituananh/.gemini/antigravity-ide/brain/d15d88ee-b909-4d48-87d6-28c49394a4f9/scratch/generate_incident_sd.py'
]

for file in files:
    with open(file, 'r') as f:
        content = f.read()
    
    # regex to match generate_xml and process_steps
    generate_xml_pattern = re.compile(r'    def generate_xml\(self\):.*?    def process_steps\(self, steps_list, active, xs, depth\):', re.DOTALL)
    process_steps_pattern = re.compile(r'    def process_steps\(self, steps_list, active, xs, depth\):.*?flows = \[\]', re.DOTALL)
    
    content = generate_xml_pattern.sub(new_generate_xml + '\n\n    def process_steps(self, steps_list, active, xs, depth):', content)
    content = process_steps_pattern.sub(new_process_steps + '\n\nflows = []', content)
    
    with open(file, 'w') as f:
        f.write(content)

print("Patched all 3 scripts successfully!")
