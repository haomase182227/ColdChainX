import xml.etree.ElementTree as ET
import math
import random

# Define entities and relationships
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

# Add missing entities from rels just in case
for r in rels_def:
    if r[0] not in entities: entities.append(r[0])
    if r[1] not in entities: entities.append(r[1])

# State: entity -> (c, r)
state = {}
grid_w, grid_h = 24, 24

# Initial random placement
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
    
    # Check entities
    for e, (c, r) in state.items():
        if (c, r) in occupied:
            cost += 100000
        occupied[(c, r)] = True
        
    # Check diamonds
    for (src, tgt, _, _, _) in rels_def:
        c1, r1 = state[src]
        c2, r2 = state[tgt]
        dist = abs(c1 - c2) + abs(r1 - r2)
        cost += dist * 10  # Preference for closer nodes
        
        # Midpoint
        mc = (c1 + c2) / 2.0
        mr = (r1 + r2) / 2.0
        if (mc, mr) in occupied:
            cost += 100000 # Collision with entity or another diamond
        occupied[(mc, mr)] = True
        
        # Penalize if diamond is not on integer grid or at least half grid
        # Actually (c1+c2)/2 is always multiple of 0.5, which is perfectly aligned!
        
        # Extra penalty if dist is 0 (should not happen due to entity collision)
        if dist == 0: cost += 100000
        
    # Penalize crossing lines!
    # A line segment is from (c1,r1) to (c2,r2). We check if it crosses any occupied node.
    for (src, tgt, _, _, _) in rels_def:
        c1, r1 = state[src]
        c2, r2 = state[tgt]
        # Bounding box of the line
        for e, (c, r) in state.items():
            if e == src or e == tgt: continue
            # check if point (c,r) lies on the segment (c1,r1) to (c2,r2)
            if c1 == c2 == c:
                if min(r1, r2) < r < max(r1, r2): cost += 5000
            elif r1 == r2 == r:
                if min(c1, c2) < c < max(c1, c2): cost += 5000
            else:
                # Diagonal crossing
                if min(c1,c2) <= c <= max(c1,c2) and min(r1,r2) <= r <= max(r1,r2):
                    # Check collinearity
                    if (c - c1) * (r2 - r1) == (r - r1) * (c2 - c1):
                        cost += 5000

    return cost

current_cost = get_cost(state)
T = 100.0
T_min = 0.01
alpha = 0.999

# Simulated Annealing
for i in range(50000):
    e = random.choice(entities)
    old_pos = state[e]
    
    # Try a new position
    new_c = max(0, min(grid_w-1, old_pos[0] + random.randint(-4, 4)))
    new_r = max(0, min(grid_h-1, old_pos[1] + random.randint(-4, 4)))
    
    state[e] = (new_c, new_r)
    new_cost = get_cost(state)
    
    if new_cost < current_cost:
        current_cost = new_cost
    else:
        prob = math.exp((current_cost - new_cost) / T)
        if random.random() < prob:
            current_cost = new_cost
        else:
            state[e] = old_pos # Revert
            
    T = T * alpha
    if T < T_min:
        break

print("Final cost:", current_cost)

# Generate XML
def create_cell(parent, id, value, x, y, w, h, style):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': f'<b>{value}</b>' if 'rhombus' not in style else value,
        'style': style,
        'vertex': '1',
        'parent': '1'
    })
    ET.SubElement(cell, 'mxGeometry', {'x': str(x), 'y': str(y), 'width': str(w), 'height': str(h), 'as': 'geometry'})

def create_edge(parent, id, source, target, label):
    cell = ET.SubElement(parent, 'mxCell', {
        'id': id,
        'value': label,
        'style': 'endArrow=none;html=1;rounded=0;edgeStyle=orthogonalEdgeStyle;labelBackgroundColor=#ffffff;fontStyle=1',
        'edge': '1',
        'parent': '1',
        'source': source,
        'target': target
    })
    ET.SubElement(cell, 'mxGeometry', {'relative': '1', 'as': 'geometry'})

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()
root.set('compressed', 'false')

for d in root.findall('diagram'):
    if d.attrib.get('name') == '0 - Overall Conceptual Data Model':
        root.remove(d)

new_diagram = ET.Element('diagram', {'id': 'page-0-overview', 'name': '0 - Overall Conceptual Data Model'})
graph_model = ET.SubElement(new_diagram, 'mxGraphModel', {
    'dx': '4000', 'dy': '4000', 'grid': '1', 'gridSize': '10', 'guides': '1',
    'tooltips': '1', 'connect': '1', 'arrows': '1', 'fold': '1', 'page': '1',
    'pageScale': '1', 'pageWidth': '4000', 'pageHeight': '4000', 'math': '0', 'shadow': '0'
})
root_el = ET.SubElement(graph_model, 'root')
ET.SubElement(root_el, 'mxCell', {'id': '0'})
ET.SubElement(root_el, 'mxCell', {'id': '1', 'parent': '0'})

CELL_W = 350
CELL_H = 200

# Entities
for e, (c, r) in state.items():
    x = c * CELL_W + 200
    y = r * CELL_H + 200
    style = 'rounded=0;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;fontSize=14;'
    create_cell(root_el, e, e, x, y, 140, 60, style)

# Relationships
for idx, (src, tgt, name, c_src, c_tgt) in enumerate(rels_def):
    c1, r1 = state[src]
    c2, r2 = state[tgt]
    mc = (c1 + c2) / 2.0
    mr = (r1 + r2) / 2.0
    x = mc * CELL_W + 200 + 20 # offset to center 100 vs 140
    y = mr * CELL_H + 200
    rel_id = f'R_{idx}'
    style = 'rhombus;whiteSpace=wrap;html=1;fillColor=#f8cecc;strokeColor=#b85450;fontSize=12;'
    create_cell(root_el, rel_id, name, x, y, 100, 60, style)
    
    create_edge(root_el, f'E_{idx}_a', src, rel_id, c_src)
    create_edge(root_el, f'E_{idx}_b', rel_id, tgt, c_tgt)

root.insert(0, new_diagram)
tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Layout finalized!")
