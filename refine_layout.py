import xml.etree.ElementTree as ET
import math
import random
import heapq

# 1. Setup entities and relationships
entities = [
    'Role', 'User', 'ChatMessage', 'Permission', 'Notification', 'MessageType', 'NotificationTemplate',
    'ServiceCatalog', 'PricingMatrix', 'ComplianceZoningRule', 'SystemConfig', 'WeightTier',
    'Customer', 'Location', 'TransportOrder', 'Invoice', 'CustomerContract', 'Quotation', 'OrderDimension',
    'InvoiceLine', 'ContractAppendix', 'TransportDocument', 'Claim', 'ClaimEvidence', 'GeoFence',
    'Warehouse', 'InboundAsn', 'WarehouseReceipt', 'Lpn', 'PenaltyBill', 'InboundReturnSlip', 'OutboundOrder', 'OutboundOrderItem',
    'RouteMaster', 'RouteStop', 'RouteSchedule', 'MasterTrip', 'Vehicle', 'TripStop', 'TripStopEvent', 'Seal',
    'TripDriver', 'Driver', 'DetentionCharge', 'ExpenseAdvance', 'ExpenseReceipt', 'DriverLicense', 'DriverWorkLog',
    'MaintenanceTicket', 'VehicleDocument', 'VehicleOdometerLog',
    'IotDevice', 'TelemetryLog', 'AlertLog', 'IncidentReport', 'IncidentEvidence', 'LpnDeliveryConfirmation', 'DeliveryEpod',
    'ReturnedItem'
]

rels_def = [
    ('Role', 'Permission', 'grants', 'M', 'N'),
    ('User', 'Role', 'assigned', 'M', 'N'),
    ('User', 'ChatMessage', 'sends', '1', 'N'),
    ('User', 'Notification', 'receives', '1', 'N'),
    ('NotificationTemplate', 'Notification', 'generates', '1', 'N'),
    ('MessageType', 'NotificationTemplate', 'classifies', '1', 'N'),
    ('Customer', 'CustomerContract', 'holds', '1', 'N'),
    ('CustomerContract', 'ContractAppendix', 'amended', '1', 'N'),
    ('Customer', 'Quotation', 'gets', '1', 'N'),
    ('Customer', 'Invoice', 'billed', '1', 'N'),
    ('Invoice', 'InvoiceLine', 'contains', '1', 'N'),
    ('Customer', 'TransportOrder', 'places', '1', 'N'),
    ('TransportOrder', 'OrderDimension', 'described', '1', '1'),
    ('TransportOrder', 'OutboundOrder', 'creates', '1', 'N'),
    ('OutboundOrder', 'OutboundOrderItem', 'contains', '1', 'N'),
    ('Location', 'GeoFence', 'defines', '1', 'N'),
    ('Location', 'Warehouse', 'located at', '1', '1'),
    ('Location', 'TransportOrder', 'origin/dest', '1', 'N'),
    ('Warehouse', 'WarehouseReceipt', 'recorded', '1', 'N'),
    ('Warehouse', 'InboundAsn', 'receives', '1', 'N'),
    ('Warehouse', 'Lpn', 'stores', '1', 'N'),
    ('TransportOrder', 'Lpn', 'contains', '1', 'N'),
    ('Lpn', 'InboundReturnSlip', 'returns', '1', 'N'),
    ('Lpn', 'PenaltyBill', 'incurs', '1', 'N'),
    ('RouteMaster', 'Location', 'stops at', 'M', 'N'),
    ('RouteMaster', 'RouteStop', 'contains', '1', 'N'),
    ('RouteMaster', 'RouteSchedule', 'offers', '1', 'N'),
    ('RouteMaster', 'MasterTrip', 'followed by', '1', 'N'),
    ('MasterTrip', 'Vehicle', 'uses', 'N', '1'),
    ('MasterTrip', 'TripStop', 'contains', '1', 'N'),
    ('TripStop', 'TripStopEvent', 'records', '1', 'N'),
    ('MasterTrip', 'TransportDocument', 'documented', '1', 'N'),
    ('MasterTrip', 'Seal', 'secured by', '1', 'N'),
    ('MasterTrip', 'TripDriver', 'staffed by', '1', 'N'),
    ('Driver', 'TripDriver', 'assigned', '1', 'N'),
    ('Vehicle', 'VehicleDocument', 'has', '1', 'N'),
    ('Vehicle', 'MaintenanceTicket', 'maintained', '1', 'N'),
    ('Vehicle', 'VehicleOdometerLog', 'records', '1', 'N'),
    ('Vehicle', 'IotDevice', 'equipped', '1', '1'),
    ('Driver', 'DriverLicense', 'holds', '1', 'N'),
    ('Driver', 'DriverWorkLog', 'records', '1', 'N'),
    ('TripDriver', 'ExpenseAdvance', 'receives', '1', 'N'),
    ('ExpenseAdvance', 'ExpenseReceipt', 'cleared', '1', 'N'),
    ('TripStop', 'DetentionCharge', 'incurs', '1', 'N'),
    ('MasterTrip', 'IncidentReport', 'experiences', '1', 'N'),
    ('IncidentReport', 'IncidentEvidence', 'supported', '1', 'N'),
    ('TransportOrder', 'Claim', 'concerns', '1', 'N'),
    ('Claim', 'ClaimEvidence', 'supported', '1', 'N'),
    ('IotDevice', 'TelemetryLog', 'produces', '1', 'N'),
    ('MasterTrip', 'AlertLog', 'triggers', '1', 'N'),
    ('Lpn', 'LpnDeliveryConfirmation', 'outcome', '1', '1'),
    ('MasterTrip', 'LpnDeliveryConfirmation', 'records', '1', 'N'),
    ('LpnDeliveryConfirmation', 'DeliveryEpod', 'signed by', '1', '1'),
    ('DeliveryEpod', 'ReturnedItem', 'records', '1', 'N'),
]

for r in rels_def:
    if r[0] not in entities: entities.append(r[0])
    if r[1] not in entities: entities.append(r[1])

# 2. Simulated Annealing for Node Placement
state = {}
grid_w, grid_h = 12, 12

used = set()
for e in entities:
    while True:
        c, r = random.randint(0, grid_w-1), random.randint(0, grid_h-1)
        if (c, r) not in used:
            state[e] = (c, r)
            used.add((c, r))
            break

def get_cost(state):
    cost = 0
    occupied = {}
    for e, (c, r) in state.items():
        if (c, r) in occupied: cost += 100000
        occupied[(c, r)] = True
        
    for (src, tgt, _, _, _) in rels_def:
        c1, r1 = state[src]
        c2, r2 = state[tgt]
        dist = abs(c1 - c2) + abs(r1 - r2)
        cost += dist * 10
        mc = (c1 + c2) / 2.0
        mr = (r1 + r2) / 2.0
        if (mc, mr) in occupied: cost += 100000
        occupied[(mc, mr)] = True
        if dist == 0: cost += 100000
        
    for (src, tgt, _, _, _) in rels_def:
        c1, r1 = state[src]
        c2, r2 = state[tgt]
        for e, (c, r) in state.items():
            if e == src or e == tgt: continue
            if c1 == c2 == c:
                if min(r1, r2) < r < max(r1, r2): cost += 5000
            elif r1 == r2 == r:
                if min(c1, c2) < c < max(c1, c2): cost += 5000
            else:
                if min(c1,c2) <= c <= max(c1,c2) and min(r1,r2) <= r <= max(r1,r2):
                    if (c - c1) * (r2 - r1) == (r - r1) * (c2 - c1):
                        cost += 5000
    return cost

current_cost = get_cost(state)
T = 100.0
T_min = 0.01
alpha = 0.999

for i in range(100000):
    e = random.choice(entities)
    old_pos = state[e]
    new_c = max(0, min(grid_w-1, old_pos[0] + random.randint(-2, 2)))
    new_r = max(0, min(grid_h-1, old_pos[1] + random.randint(-2, 2)))
    state[e] = (new_c, new_r)
    new_cost = get_cost(state)
    if new_cost < current_cost:
        current_cost = new_cost
    else:
        prob = math.exp((current_cost - new_cost) / T)
        if random.random() < prob:
            current_cost = new_cost
        else:
            state[e] = old_pos
    T = T * alpha
    if T < T_min: break

print("SA Final cost:", current_cost)

# 3. Build XML structure
CELL_W = 500
CELL_H = 300

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()
root.set('compressed', 'false')

for d in root.findall('diagram'):
    if d.attrib.get('name') == '0 - Overall Conceptual Data Model':
        root.remove(d)

new_diagram = ET.Element('diagram', {'id': 'page-0-overview', 'name': '0 - Overall Conceptual Data Model'})
graph_model = ET.SubElement(new_diagram, 'mxGraphModel', {
    'dx': '8000', 'dy': '5000', 'grid': '1', 'gridSize': '10', 'guides': '1',
    'tooltips': '1', 'connect': '1', 'arrows': '1', 'fold': '1', 'page': '1',
    'pageScale': '1', 'pageWidth': '8000', 'pageHeight': '5000', 'math': '0', 'shadow': '0'
})
root_el = ET.SubElement(graph_model, 'root')
ET.SubElement(root_el, 'mxCell', {'id': '0'})
ET.SubElement(root_el, 'mxCell', {'id': '1', 'parent': '0'})

cells_pos = {}

for e, (c, r) in state.items():
    x = c * CELL_W + 200
    y = r * CELL_H + 200
    w, h = 140, 60
    cells_pos[e] = {'x': x, 'y': y, 'w': w, 'h': h}
    cell = ET.SubElement(root_el, 'mxCell', {
        'id': e, 'value': f'<b>{e}</b>',
        'style': 'rounded=0;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=14;',
        'vertex': '1', 'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

for idx, (src, tgt, name, c_src, c_tgt) in enumerate(rels_def):
    c1, r1 = state[src]
    c2, r2 = state[tgt]
    mc = (c1 + c2) / 2.0
    mr = (r1 + r2) / 2.0
    # Center 100x60 relative to 140x60
    x = mc * CELL_W + 200 + 20 
    y = mr * CELL_H + 200
    w, h = 100, 60
    rel_id = f'R_{idx}'
    cells_pos[rel_id] = {'x': x, 'y': y, 'w': w, 'h': h}
    cell = ET.SubElement(root_el, 'mxCell', {
        'id': rel_id, 'value': name,
        'style': 'rhombus;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;fontSize=12;',
        'vertex': '1', 'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

# 4. A* Routing
grid_size = 20
obstacles = set()
for c in cells_pos.values():
    gx = int(c['x'] // grid_size)
    gy = int(c['y'] // grid_size)
    gw = int(c['w'] // grid_size)
    gh = int(c['h'] // grid_size)
    for i in range(gx - 2, gx + gw + 3):
        for j in range(gy - 2, gy + gh + 3):
            obstacles.add((i, j))

used_paths = {}
def get_neighbors(x, y):
    return [(x+1, y, 1, 0), (x-1, y, -1, 0), (x, y+1, 0, 1), (x, y-1, 0, -1)]

def route_edge(sx, sy, tx, ty, src_id, tgt_id):
    sgx, sgy = int(sx // grid_size), int(sy // grid_size)
    tgx, tgy = int(tx // grid_size), int(ty // grid_size)
    src_c = cells_pos[src_id]
    tgt_c = cells_pos[tgt_id]
    temp_free = set()
    for c in [src_c, tgt_c]:
        cx = int(c['x'] // grid_size)
        cy = int(c['y'] // grid_size)
        cw = int(c['w'] // grid_size)
        ch = int(c['h'] // grid_size)
        for i in range(cx - 2, cx + cw + 3):
            for j in range(cy - 2, cy + ch + 3):
                if (i, j) in obstacles:
                    obstacles.remove((i, j))
                    temp_free.add((i, j))
                    
    pq = []
    heapq.heappush(pq, (0, 0, sgx, sgy, 0, 0, None))
    came_from = {}
    g_score = {(sgx, sgy): 0}
    found = False
    while pq:
        _, g, x, y, dx, dy, p = heapq.heappop(pq)
        if (x, y) in came_from: continue
        came_from[(x, y)] = (p, dx, dy)
        if x == tgx and y == tgy:
            found = True
            break
        for nx, ny, ndx, ndy in get_neighbors(x, y):
            if (nx, ny) in obstacles: continue
            turn_penalty = 15 if (dx != 0 or dy != 0) and (ndx != dx or ndy != dy) else 0
            usage_penalty = used_paths.get((nx, ny), 0) * 100
            new_g = g + 1 + turn_penalty + usage_penalty
            if (nx, ny) not in g_score or new_g < g_score[(nx, ny)]:
                g_score[(nx, ny)] = new_g
                h = abs(nx - tgx) + abs(ny - tgy)
                heapq.heappush(pq, (new_g + h, new_g, nx, ny, ndx, ndy, (x, y)))
                
    for p in temp_free: obstacles.add(p)
    if not found: return []
    curr = (tgx, tgy)
    path = []
    while curr is not None:
        path.append(curr)
        if curr not in came_from: break
        curr = came_from[curr][0]
    path.reverse()
    for p in path: used_paths[p] = used_paths.get(p, 0) + 1
    corners = []
    if len(path) > 2:
        for i in range(1, len(path)-1):
            px, py = path[i-1]
            cx, cy = path[i]
            nx, ny = path[i+1]
            if (cx - px) != (nx - cx) or (cy - py) != (ny - cy):
                corners.append((cx * grid_size, cy * grid_size))
    return corners

for idx, (src, tgt, name, c_src, c_tgt) in enumerate(rels_def):
    rel_id = f'R_{idx}'
    
    # Edge A: Src -> Rel
    s_c = cells_pos[src]
    t_c = cells_pos[rel_id]
    sx = s_c['x'] + s_c['w']/2
    sy = s_c['y'] + s_c['h']/2
    tx = t_c['x'] + t_c['w']/2
    ty = t_c['y'] + t_c['h']/2
    corners_a = route_edge(sx, sy, tx, ty, src, rel_id)
    cell_a = ET.SubElement(root_el, 'mxCell', {
        'id': f'E_{idx}_a', 'value': c_src,
        'style': 'endArrow=none;html=1;rounded=0;edgeStyle=orthogonalEdgeStyle;labelBackgroundColor=#ffffff;fontStyle=1',
        'edge': '1', 'parent': '1', 'source': src, 'target': rel_id
    })
    geo_a = ET.SubElement(cell_a, 'mxGeometry', {'relative': '1', 'as': 'geometry'})
    if corners_a:
        arr = ET.SubElement(geo_a, 'Array', {'as': 'points'})
        for cx, cy in corners_a: ET.SubElement(arr, 'mxPoint', {'x': str(cx), 'y': str(cy)})

    # Edge B: Rel -> Tgt
    s_c = cells_pos[rel_id]
    t_c = cells_pos[tgt]
    sx = s_c['x'] + s_c['w']/2
    sy = s_c['y'] + s_c['h']/2
    tx = t_c['x'] + t_c['w']/2
    ty = t_c['y'] + t_c['h']/2
    corners_b = route_edge(sx, sy, tx, ty, rel_id, tgt)
    cell_b = ET.SubElement(root_el, 'mxCell', {
        'id': f'E_{idx}_b', 'value': c_tgt,
        'style': 'endArrow=none;html=1;rounded=0;edgeStyle=orthogonalEdgeStyle;labelBackgroundColor=#ffffff;fontStyle=1',
        'edge': '1', 'parent': '1', 'source': rel_id, 'target': tgt
    })
    geo_b = ET.SubElement(cell_b, 'mxGeometry', {'relative': '1', 'as': 'geometry'})
    if corners_b:
        arr = ET.SubElement(geo_b, 'Array', {'as': 'points'})
        for cx, cy in corners_b: ET.SubElement(arr, 'mxPoint', {'x': str(cx), 'y': str(cy)})

root.insert(0, new_diagram)
tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Final refinement complete!")
