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
        xml = f'''  <diagram name="{self.title}" id="{self.title.replace(' ', '_')}">
    <mxGraphModel dx="1000" dy="1000" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{math.ceil(self.page_width)}" pageHeight="{math.ceil(self.page_height)}" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />
        <mxCell id="title" value="{self.title}" style="text;html=1;align=left;verticalAlign=middle;whiteSpace=wrap;fontSize=20;fontStyle=1;fontColor=#000000;" parent="1" vertex="1" connectable="0">
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
        </mxCell>\n'''
            else:
                style = "rounded=1;whiteSpace=wrap;html=1;align=center;verticalAlign=middle;fillColor=#ffffff;strokeColor=#000000;fontColor=#000000;strokeWidth=1;fontSize=14;fontStyle=1;"
                xml += f'''        <mxCell id="p_{p.replace(' ', '_')}" value="{p}" style="{style}" parent="1" vertex="1">
          <mxGeometry x="{cx - self.part_width/2}" y="{self.start_y - self.part_height}" width="{self.part_width}" height="{self.part_height}" as="geometry" />
        </mxCell>\n'''
            
            xml += f'''        <mxCell id="lend_{p.replace(' ', '_')}" value="" style="opacity=0;fillOpacity=0;strokeOpacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{cx}" y="{self.start_y + len(self.steps) * self.spacing_y + 40}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="lline_{p.replace(' ', '_')}" value="" style="edgeStyle=none;html=1;dashed=1;dashPattern=8 8;endArrow=none;startArrow=none;strokeColor=#000000;strokeWidth=1;" parent="1" source="p_{p.replace(' ', '_')}" target="lend_{p.replace(' ', '_')}" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>\n'''

        active = {p: [] for p in self.participants}
        act_counter = 0
        y = self.start_y
        msg_xml = ""
        act_xml = ""
        
        for i, step in enumerate(self.steps):
            src, tgt, text, is_return = step
            y += self.spacing_y
            
            if is_return and active[src]:
                act = active[src].pop()
                act['end_y'] = y
                act_xml += f'''        <mxCell id="{act['id']}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#ffffff;strokeColor=#000000;strokeWidth=1;" parent="1" vertex="1">
          <mxGeometry x="{xs[src] - self.act_width/2}" y="{act['start_y']}" width="{self.act_width}" height="{act['end_y'] - act['start_y']}" as="geometry" />
        </mxCell>\n'''
            
            if not is_return:
                act_id = f"act_{act_counter}"
                act_counter += 1
                active[tgt].append({'id': act_id, 'start_y': y})
            
            msg_id = f"msg_{i}"
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
                
            msg_xml += f'''        <mxCell id="{msg_id}_sp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{sp_x}" y="{y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_tp" value="" style="opacity=0;" parent="1" vertex="1" connectable="0">
          <mxGeometry x="{tp_x}" y="{y}" width="1" height="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_edge" value="" style="{style}" parent="1" source="{msg_id}_sp" target="{msg_id}_tp" edge="1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="{msg_id}_lbl" value="{text}" style="text;html=1;align=center;verticalAlign=bottom;fontSize=12;fontColor=#000000;labelBackgroundColor=none;" parent="{msg_id}_edge" vertex="1" connectable="0">
          <mxGeometry y="-10" x="0" relative="1" as="geometry" />
        </mxCell>\n'''

        y += self.spacing_y
        for p in self.participants:
            while active[p]:
                act = active[p].pop()
                act['end_y'] = y
                act_xml += f'''        <mxCell id="{act['id']}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;fillColor=#ffffff;strokeColor=#000000;strokeWidth=1;" parent="1" vertex="1">
          <mxGeometry x="{xs[p] - self.act_width/2}" y="{act['start_y']}" width="{self.act_width}" height="{act['end_y'] - act['start_y']}" as="geometry" />
        </mxCell>\n'''
                
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

flows.append({
    "name": "Register User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "Submit Registration", False),
        ("App", "AuthController", "POST /api/v1/auth/register", False),
        ("AuthController", "AuthService", "RegisterAsync()", False),
        ("AuthService", "UserRepository", "Create User", False),
        ("UserRepository", "PostgreSQL", "SQL INSERT", False),
        ("PostgreSQL", "UserRepository", "Rows Affected", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "AuthController", "AuthResponseDto", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Registration Success", True)
    ]
})

flows.append({
    "name": "Login User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "JwtService", "PostgreSQL"],
    "steps": [
        ("User", "App", "Submit Login", False),
        ("App", "AuthController", "POST /api/v1/auth/login", False),
        ("AuthController", "AuthService", "LoginAsync()", False),
        ("AuthService", "UserRepository", "Get User by Email", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "JwtService", "GenerateAccessToken()", False),
        ("JwtService", "AuthService", "Access Token", True),
        ("AuthService", "JwtService", "GenerateRefreshToken()", False),
        ("JwtService", "AuthService", "Refresh Token", True),
        ("AuthService", "UserRepository", "Update RefreshToken", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "AuthService", "Success", True),
        ("AuthService", "AuthController", "AuthResponseDto", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Dashboard", True)
    ]
})

flows.append({
    "name": "Google Login Mobile Sequence Diagram",
    "participants": ["User", "App", "Google", "AuthController", "GoogleAuthService", "GoogleIdTokenValidator", "UserRepository", "JwtService"],
    "steps": [
        ("User", "App", "Login with Google", False),
        ("App", "Google", "Get ID Token", False),
        ("Google", "App", "id_token", True),
        ("App", "AuthController", "POST /api/v1/auth/google-login", False),
        ("AuthController", "GoogleAuthService", "AuthenticateAsync(id_token)", False),
        ("GoogleAuthService", "GoogleIdTokenValidator", "ValidateAsync(id_token)", False),
        ("GoogleIdTokenValidator", "GoogleAuthService", "VerifiedGoogleUserDto", True),
        ("GoogleAuthService", "UserRepository", "GetByGoogleIdAsync()", False),
        ("UserRepository", "GoogleAuthService", "User Entity", True),
        ("GoogleAuthService", "JwtService", "GenerateTokens()", False),
        ("JwtService", "GoogleAuthService", "Tokens", True),
        ("GoogleAuthService", "AuthController", "GoogleLoginResponse", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Dashboard", True)
    ]
})

flows.append({
    "name": "Google OAuth Web Sequence Diagram",
    "participants": ["User", "Browser", "AuthController", "GoogleOAuthClient", "Google", "GoogleAuthService", "JwtService"],
    "steps": [
        ("User", "Browser", "Click Google Login", False),
        ("Browser", "AuthController", "GET /api/v1/auth/google-auth", False),
        ("AuthController", "GoogleOAuthClient", "GetAuthorizationUrl()", False),
        ("GoogleOAuthClient", "AuthController", "URL", True),
        ("AuthController", "Browser", "302 Redirect to Google", True),
        ("Browser", "Google", "User Grants Access", False),
        ("Google", "AuthController", "GET /api/v1/auth/google/callback", False),
        ("AuthController", "Browser", "302 Redirect to Frontend?code", True),
        ("Browser", "AuthController", "POST /api/v1/auth/google/exchange", False),
        ("AuthController", "GoogleOAuthClient", "ExchangeCodeForIdTokenAsync(code)", False),
        ("GoogleOAuthClient", "AuthController", "id_token", True),
        ("AuthController", "GoogleAuthService", "AuthenticateAsync(id_token)", False),
        ("GoogleAuthService", "JwtService", "GenerateTokens()", False),
        ("JwtService", "GoogleAuthService", "Tokens", True),
        ("GoogleAuthService", "AuthController", "GoogleLoginResponse", True),
        ("AuthController", "Browser", "200 OK", True),
        ("Browser", "User", "Dashboard", True)
    ]
})

flows.append({
    "name": "Logout User Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "Click Logout", False),
        ("App", "AuthController", "POST /api/v1/auth/logout", False),
        ("AuthController", "AuthService", "LogoutAsync(userId)", False),
        ("AuthService", "UserRepository", "GetUserByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "UserRepository", "Clear RefreshToken", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "AuthService", "Success", True),
        ("AuthService", "AuthController", "True", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Login Screen", True)
    ]
})

flows.append({
    "name": "Refresh Token Sequence Diagram",
    "participants": ["App", "AuthController", "AuthService", "UserRepository", "JwtService", "PostgreSQL"],
    "steps": [
        ("App", "AuthController", "POST /api/v1/auth/refresh-tokens", False),
        ("AuthController", "AuthService", "RefreshTokensAsync()", False),
        ("AuthService", "UserRepository", "GetByRefreshTokenAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "JwtService", "GenerateAccessToken()", False),
        ("JwtService", "AuthService", "Access Token", True),
        ("AuthService", "JwtService", "GenerateRefreshToken()", False),
        ("JwtService", "AuthService", "Refresh Token", True),
        ("AuthService", "UserRepository", "Update RefreshToken", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "AuthService", "Success", True),
        ("AuthService", "AuthController", "AuthResponseDto", True),
        ("AuthController", "App", "200 OK (New Tokens)", True)
    ]
})

flows.append({
    "name": "Get Profile Sequence Diagram",
    "participants": ["User", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "View Profile", False),
        ("App", "UserController", "GET /api/v1/users/{id}", False),
        ("UserController", "UserService", "GetUserByIdAsync(id)", False),
        ("UserService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "UserService", "User Entity", True),
        ("UserService", "UserController", "UserProfileDto", True),
        ("UserController", "App", "200 OK", True),
        ("App", "User", "Display Profile", True)
    ]
})

flows.append({
    "name": "Update Profile Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "Submit Profile Edit", False),
        ("App", "AuthController", "PUT /api/v1/auth/profile", False),
        ("AuthController", "AuthService", "UpdateUserAsync()", False),
        ("AuthService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "UserRepository", "Update User", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "AuthService", "Success", True),
        ("AuthService", "AuthController", "UserProfileDto", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Show Success", True)
    ]
})

flows.append({
    "name": "Change Password Sequence Diagram",
    "participants": ["User", "App", "AuthController", "AuthService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("User", "App", "Submit New Password", False),
        ("App", "AuthController", "PUT /api/v1/auth/change-password", False),
        ("AuthController", "AuthService", "ChangePasswordAsync()", False),
        ("AuthService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "AuthService", "User Entity", True),
        ("AuthService", "UserRepository", "Update Password Hash", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "AuthService", "Success", True),
        ("AuthService", "AuthController", "True", True),
        ("AuthController", "App", "200 OK", True),
        ("App", "User", "Show Success", True)
    ]
})

flows.append({
    "name": "Reset Password Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "Request Reset Password", False),
        ("App", "UserController", "POST /api/v1/users/{id}/reset-password", False),
        ("UserController", "UserService", "ResetPasswordAsync()", False),
        ("UserService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "UserService", "User Entity", True),
        ("UserService", "UserRepository", "Update Password Hash", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "UserService", "Success", True),
        ("UserService", "UserController", "True", True),
        ("UserController", "App", "200 OK", True),
        ("App", "Admin", "Show Success", True)
    ]
})

flows.append({
    "name": "Change User Status Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "Toggle Status", False),
        ("App", "UserController", "PATCH /api/v1/users/{id}/status", False),
        ("UserController", "UserService", "ChangeStatusAsync()", False),
        ("UserService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "UserService", "User Entity", True),
        ("UserService", "UserRepository", "Update Status", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "UserService", "Success", True),
        ("UserService", "UserController", "True", True),
        ("UserController", "App", "200 OK", True),
        ("App", "Admin", "Show Success", True)
    ]
})

flows.append({
    "name": "Change User Role Sequence Diagram",
    "participants": ["Admin", "App", "UserController", "UserService", "UserRepository", "PostgreSQL"],
    "steps": [
        ("Admin", "App", "Select New Role", False),
        ("App", "UserController", "PATCH /api/v1/users/{id}/role", False),
        ("UserController", "UserService", "ChangeRoleAsync()", False),
        ("UserService", "UserRepository", "GetByIdAsync()", False),
        ("UserRepository", "PostgreSQL", "SQL SELECT", False),
        ("PostgreSQL", "UserRepository", "User Data", True),
        ("UserRepository", "UserService", "User Entity", True),
        ("UserService", "UserRepository", "Update RoleId", False),
        ("UserRepository", "PostgreSQL", "SQL UPDATE", False),
        ("PostgreSQL", "UserRepository", "DB Saved", True),
        ("UserRepository", "UserService", "Success", True),
        ("UserService", "UserController", "True", True),
        ("UserController", "App", "200 OK", True),
        ("App", "Admin", "Show Success", True)
    ]
})

output = '<?xml version="1.0" encoding="UTF-8"?>\n<mxfile>\n'
for flow in flows:
    gen = DiagramGenerator(flow["name"], flow["participants"], flow["steps"])
    output += gen.generate_xml() + '\n'
output += '</mxfile>'

with open('docs/Sequence-Diagram-Authentication-User-Management.drawio', 'w', encoding='utf-8') as f:
    f.write(output)

print(f"Generated {len(flows)} diagrams successfully!")
