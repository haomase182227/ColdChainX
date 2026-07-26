import math

class DiagramGenerator:
    def __init__(self, title, participants, steps):
        self.title = title
        self.participants = participants
        self.steps = steps
        
        self.spacing_x = 220
        self.start_y = 150
        self.spacing_y = 50
        self.act_width = 10
        self.part_width = 120
        self.part_height = 40
        self.page_width = max(1169, 100 + len(participants) * self.spacing_x + 100)
        self.page_height = max(827, self.start_y + len(steps) * self.spacing_y + 100)
        
    def generate_xml(self):
        # Header
        xml = f'''  <diagram name="{self.title}" id="{self.title.replace(' ', '_')}">
    <mxGraphModel dx="1000" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{math.ceil(self.page_width)}" pageHeight="{math.ceil(self.page_height)}" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />
        <mxCell id="title" value="{self.title}" style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=20;fontStyle=1;fontColor=#000000;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="20" y="20" width="500" height="30" as="geometry" />
        </mxCell>
'''
        # Participants & Lifelines
        xs = {}
        for i, p in enumerate(self.participants):
            cx = 100 + i * self.spacing_x
            xs[p] = cx
            is_actor = (i == 0)
            
            if is_actor:
                style = "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;fillColor=#ffffff;strokeColor=#000000;fontColor=#000000;strokeWidth=1;fontSize=14;"
                xml += f'''        <mxCell id="p_{p}" value="{p}" style="{style}" parent="1" vertex="1">
          <mxGeometry x="{cx - 20}" y="{self.start_y - 60}" width="40" height="80" as="geometry" />
        </mxCell>\n'''
            else:
                style = "rounded=1;whiteSpace=wrap;html=1;align=center;verticalAlign=middle;fillColor=#ffffff;strokeColor=#000000;fontColor=#000000;strokeWidth=1;fontSize=14;fontStyle=1;"
                xml += f'''        <mxCell id="p_{p}" value="{p}" style="{style}" parent="1" vertex="1">
          <mxGeometry x="{cx - self.part_width/2}" y="{self.start_y - self.part_height}" width="{self.part_width}" height="{self.part_height}" as="geometry" />
        </mxCell>\n'''
            
            # Lifeline
            xml += f'''        <mxCell id="lend_{p}" value="" style="opacity=0;fillOpacity=0;strokeOpacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{cx}" y="{self.start_y + len(self.steps) * self.spacing_y + 40}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="lline_{p}" value="" style="edgeStyle=none;html=1;dashed=1;dashPattern=8 8;endArrow=none;startArrow=none;strokeColor=#000000;strokeWidth=1;" parent="1" source="p_{p}" target="lend_{p}" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>\n'''

        # Activation bars
        active = {p: [] for p in self.participants}
        act_counter = 0
        
        # Messages
        y = self.start_y
        msg_xml = ""
        act_xml = ""
        
        for i, step in enumerate(self.steps):
            src, tgt, text, is_return = step
            y += self.spacing_y
            
            # End activation of src if return
            if is_return and active[src]:
                act = active[src].pop()
                act['end_y'] = y
                act_xml += f'''        <mxCell id="{act['id']}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#ffffff;strokeColor=#000000;strokeWidth=1;" parent="1" vertex="1">
          <mxGeometry x="{xs[src] - self.act_width/2}" y="{act['start_y']}" width="{self.act_width}" height="{act['end_y'] - act['start_y']}" as="geometry" />
        </mxCell>\n'''
            
            # Start activation for tgt if request
            if not is_return:
                act_id = f"act_{act_counter}"
                act_counter += 1
                active[tgt].append({'id': act_id, 'start_y': y})
            
            # Message Edge
            msg_id = f"msg_{i}"
            sp_x = xs[src]
            tp_x = xs[tgt]
            
            # offset for activation bars
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
                
            msg_xml += f'''        <mxCell id="{msg_id}_sp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{sp_x}" y="{y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_tp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{tp_x}" y="{y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_edge" value="" style="{style}" parent="1" source="{msg_id}_sp" target="{msg_id}_tp" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_lbl" value="{text}" style="text;html=1;align=center;verticalAlign=bottom;fontSize=12;fontColor=#000000;labelBackgroundColor=#ffffff;" parent="{msg_id}_edge" vertex="1" connectable="0">
          <mxGeometry relative="1" y="-5" as="geometry" />
        </mxCell>\n'''

        # Close remaining activations
        y += self.spacing_y
        for p in self.participants:
            while active[p]:
                act = active[p].pop()
                act['end_y'] = y
                act_xml += f'''        <mxCell id="{act['id']}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#ffffff;strokeColor=#000000;strokeWidth=1;" parent="1" vertex="1">
          <mxGeometry x="{xs[p] - self.act_width/2}" y="{act['start_y']}" width="{self.act_width}" height="{act['end_y'] - act['start_y']}" as="geometry" />
        </mxCell>\n'''
                
        # Caption
        cap_y = y + 60
        xml += act_xml + msg_xml
        xml += f'''        <mxCell id="cap_{self.title.replace(' ', '_')}" value="Figure: {self.title}" style="text;html=1;align=center;verticalAlign=middle;whiteSpace=wrap;fontSize=14;fontStyle=2;fontColor=#000000;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="0" y="{cap_y}" width="{self.page_width}" height="30" as="geometry" />
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>'''
        return xml

flows = []

# 1. Register
flows.append({
    "name": "Register User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "1. Submit Registration", False),
        ("App", "AuthController", "2. POST /api/v1/auth/register", False),
        ("AuthController", "AuthService", "3. RegisterAsync()", False),
        ("AuthService", "UserRepository", "4. Create User", False),
        ("UserRepository", "PostgreSQL", "5. SQL INSERT", False),
        ("PostgreSQL", "UserRepository", "6. Rows Affected", True),
        ("UserRepository", "AuthService", "7. User Entity", True),
        ("AuthService", "AuthController", "8. AuthResponseDto", True),
        ("AuthController", "App", "9. 200 OK", True),
        ("App", "User", "10. Registration Success", True)
    ]
})

# 2. Login
flows.append({
    "name": "Login User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "JwtService", "PostgreSQL"],
    "steps": [
        ("User", "App", "1. Submit Login", False),
        ("App", "AuthController", "2. POST /api/v1/auth/login", False),
        ("AuthController", "AuthService", "3. LoginAsync()", False),
        ("AuthService", "UserRepository", "4. Get User by Email", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "AuthService", "7. User Entity", True),
        ("AuthService", "JwtService", "8. GenerateAccessToken()", False),
        ("JwtService", "AuthService", "9. Access Token", True),
        ("AuthService", "JwtService", "10. GenerateRefreshToken()", False),
        ("JwtService", "AuthService", "11. Refresh Token", True),
        ("AuthService", "UserRepository", "12. Update RefreshToken", False),
        ("UserRepository", "PostgreSQL", "13. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "14. DB Saved", True),
        ("UserRepository", "AuthService", "15. Success", True),
        ("AuthService", "AuthController", "16. AuthResponseDto", True),
        ("AuthController", "App", "17. 200 OK", True),
        ("App", "User", "18. Dashboard", True)
    ]
})

# 3. Logout
flows.append({
    "name": "Logout User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "1. Click Logout", False),
        ("App", "AuthController", "2. POST /api/v1/auth/logout", False),
        ("AuthController", "AuthService", "3. LogoutAsync(userId)", False),
        ("AuthService", "UserRepository", "4. GetUserByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "AuthService", "7. User Entity", True),
        ("AuthService", "UserRepository", "8. Clear RefreshToken", False),
        ("UserRepository", "PostgreSQL", "9. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "10. DB Saved", True),
        ("UserRepository", "AuthService", "11. Success", True),
        ("AuthService", "AuthController", "12. True", True),
        ("AuthController", "App", "13. 200 OK", True),
        ("App", "User", "14. Login Screen", True)
    ]
})

# 4. Refresh Token
flows.append({
    "name": "Refresh Token Sequence Diagram",
    "participants": ["App", "AuthController", "AuthService", "UserRepository", "JwtService", "PostgreSQL"],
    "steps": [
        ("App", "AuthController", "1. POST /api/v1/auth/refresh-tokens", False),
        ("AuthController", "AuthService", "2. RefreshTokensAsync()", False),
        ("AuthService", "UserRepository", "3. GetByRefreshTokenAsync()", False),
        ("UserRepository", "PostgreSQL", "4. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "5. User Data", True),
        ("UserRepository", "AuthService", "6. User Entity", True),
        ("AuthService", "JwtService", "7. GenerateAccessToken()", False),
        ("JwtService", "AuthService", "8. Access Token", True),
        ("AuthService", "JwtService", "9. GenerateRefreshToken()", False),
        ("JwtService", "AuthService", "10. Refresh Token", True),
        ("AuthService", "UserRepository", "11. Update RefreshToken", False),
        ("UserRepository", "PostgreSQL", "12. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "13. DB Saved", True),
        ("UserRepository", "AuthService", "14. Success", True),
        ("AuthService", "AuthController", "15. AuthResponseDto", True),
        ("AuthController", "App", "16. 200 OK (New Tokens)", True)
    ]
})

# 5. Get Current User/Profile
flows.append({
    "name": "Get Profile Sequence Diagram",
    "participants": ["User", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "1. View Profile", False),
        ("App", "UserController", "2. GET /api/v1/users/{id}", False),
        ("UserController", "UserService", "3. GetUserByIdAsync(id)", False),
        ("UserService", "UserRepository", "4. GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "UserService", "7. User Entity", True),
        ("UserService", "UserController", "8. UserProfileDto", True),
        ("UserController", "App", "9. 200 OK", True),
        ("App", "User", "10. Display Profile", True)
    ]
})

# 6. Update User/Profile
flows.append({
    "name": "Update Profile Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "1. Submit Profile Edit", False),
        ("App", "AuthController", "2. PUT /api/v1/auth/profile", False),
        ("AuthController", "AuthService", "3. UpdateUserAsync()", False),
        ("AuthService", "UserRepository", "4. GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "AuthService", "7. User Entity", True),
        ("AuthService", "UserRepository", "8. Update User", False),
        ("UserRepository", "PostgreSQL", "9. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "10. DB Saved", True),
        ("UserRepository", "AuthService", "11. Success", True),
        ("AuthService", "AuthController", "12. UserProfileDto", True),
        ("AuthController", "App", "13. 200 OK", True),
        ("App", "User", "14. Show Success", True)
    ]
})

# 7. Reset Password
flows.append({
    "name": "Reset Password Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "1. Request Reset Password", False),
        ("App", "UserController", "2. POST /api/v1/users/{id}/reset-password", False),
        ("UserController", "UserService", "3. ResetPasswordAsync()", False),
        ("UserService", "UserRepository", "4. GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "UserService", "7. User Entity", True),
        ("UserService", "UserRepository", "8. Update Password Hash", False),
        ("UserRepository", "PostgreSQL", "9. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "10. DB Saved", True),
        ("UserRepository", "UserService", "11. Success", True),
        ("UserService", "UserController", "12. True", True),
        ("UserController", "App", "13. 200 OK", True),
        ("App", "Admin", "14. Show Success", True)
    ]
})

# 8. Activate / Deactivate User
flows.append({
    "name": "Change User Status Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "1. Toggle Status", False),
        ("App", "UserController", "2. PATCH /api/v1/users/{id}/status", False),
        ("UserController", "UserService", "3. ChangeStatusAsync()", False),
        ("UserService", "UserRepository", "4. GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "UserService", "7. User Entity", True),
        ("UserService", "UserRepository", "8. Update Status", False),
        ("UserRepository", "PostgreSQL", "9. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "10. DB Saved", True),
        ("UserRepository", "UserService", "11. Success", True),
        ("UserService", "UserController", "12. True", True),
        ("UserController", "App", "13. 200 OK", True),
        ("App", "Admin", "14. Show Success", True)
    ]
})

# 9. Assign or Update Role
flows.append({
    "name": "Change User Role Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "1. Select New Role", False),
        ("App", "UserController", "2. PATCH /api/v1/users/{id}/role", False),
        ("UserController", "UserService", "3. ChangeRoleAsync()", False),
        ("UserService", "UserRepository", "4. GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "5. SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "6. User Data", True),
        ("UserRepository", "UserService", "7. User Entity", True),
        ("UserService", "UserRepository", "8. Update RoleId", False),
        ("UserRepository", "PostgreSQL", "9. SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "10. DB Saved", True),
        ("UserRepository", "UserService", "11. Success", True),
        ("UserService", "UserController", "12. True", True),
        ("UserController", "App", "13. 200 OK", True),
        ("App", "Admin", "14. Show Success", True)
    ]
})


output = '<?xml version="1.0" encoding="UTF-8"?>\n<mxfile>\n'
for flow in flows:
    gen = DiagramGenerator(flow["name"], flow["participants"], flow["steps"])
    output += gen.generate_xml() + '\n'
output += '</mxfile>'

with open('docs/Sequence-Diagram-Authentication-User-Management.drawio', 'w', encoding='utf-8') as f:
    f.write(output)

print("Generated docs/Sequence-Diagram-Authentication-User-Management.drawio successfully!")
