import xml.etree.ElementTree as ET
import heapq

tree = ET.parse('docs/Conceptual-Data-Model.drawio')
root = tree.getroot()
diagrams = root.findall('diagram')

d_model = None
for d in diagrams:
    if d.attrib.get('name') == '0 - Overall Conceptual Data Model':
        d_model = d.find('mxGraphModel')
        break

root_el = d_model.find('root')

# 1. Scale layout
SCALE = 2.0
cells = {}
for cell in root_el.findall('mxCell'):
    cid = cell.attrib.get('id')
    geo = cell.find('mxGeometry')
    if geo is not None and cell.attrib.get('vertex') == '1':
        x = float(geo.attrib.get('x', 0))
        y = float(geo.attrib.get('y', 0))
        # Center of scaling is 0,0
        geo.attrib['x'] = str(int(x * SCALE))
        geo.attrib['y'] = str(int(y * SCALE))
        cells[cid] = {
            'x': int(x * SCALE),
            'y': int(y * SCALE),
            'w': int(geo.attrib.get('width', 0)),
            'h': int(geo.attrib.get('height', 0))
        }

# Update canvas size
dx = int(d_model.attrib.get('dx', 4000))
dy = int(d_model.attrib.get('dy', 4000))
d_model.attrib['dx'] = str(int(dx * SCALE))
d_model.attrib['dy'] = str(int(dy * SCALE))
d_model.attrib['pageWidth'] = str(int(dx * SCALE))
d_model.attrib['pageHeight'] = str(int(dy * SCALE))

# 2. A* Routing
grid_size = 20
obstacles = set()
for c in cells.values():
    gx = c['x'] // grid_size
    gy = c['y'] // grid_size
    gw = c['w'] // grid_size
    gh = c['h'] // grid_size
    for i in range(gx - 1, gx + gw + 2):
        for j in range(gy - 1, gy + gh + 2):
            obstacles.add((i, j))

used_paths = {} # (x, y) -> count

def get_neighbors(x, y):
    return [(x+1, y, 1, 0), (x-1, y, -1, 0), (x, y+1, 0, 1), (x, y-1, 0, -1)]

def route_edge(sx, sy, tx, ty, src_id, tgt_id):
    # start/end on grid
    sgx, sgy = sx // grid_size, sy // grid_size
    tgx, tgy = tx // grid_size, ty // grid_size
    
    # We must allow the path to start/end inside the obstacle box of the source/target
    # So we temporarily remove the source and target bounding boxes from obstacles
    src_c = cells[src_id]
    tgt_c = cells[tgt_id]
    
    temp_free = set()
    for c in [src_c, tgt_c]:
        cx = c['x'] // grid_size
        cy = c['y'] // grid_size
        cw = c['w'] // grid_size
        ch = c['h'] // grid_size
        for i in range(cx - 1, cx + cw + 2):
            for j in range(cy - 1, cy + ch + 2):
                if (i, j) in obstacles:
                    obstacles.remove((i, j))
                    temp_free.add((i, j))
                    
    # A* search
    pq = []
    heapq.heappush(pq, (0, 0, sgx, sgy, 0, 0, None)) # (f, g, x, y, dx, dy, parent)
    came_from = {}
    g_score = {(sgx, sgy): 0}
    
    found = False
    while pq:
        _, g, x, y, dx, dy, p = heapq.heappop(pq)
        
        if (x, y) in came_from:
            continue
        came_from[(x, y)] = (p, dx, dy)
        
        if x == tgx and y == tgy:
            found = True
            break
            
        for nx, ny, ndx, ndy in get_neighbors(x, y):
            if (nx, ny) in obstacles:
                continue
                
            turn_penalty = 10 if (dx != 0 or dy != 0) and (ndx != dx or ndy != dy) else 0
            usage_penalty = used_paths.get((nx, ny), 0) * 50
            
            new_g = g + 1 + turn_penalty + usage_penalty
            if (nx, ny) not in g_score or new_g < g_score[(nx, ny)]:
                g_score[(nx, ny)] = new_g
                h = abs(nx - tgx) + abs(ny - tgy)
                heapq.heappush(pq, (new_g + h, new_g, nx, ny, ndx, ndy, (x, y)))
                
    # Restore obstacles
    for p in temp_free:
        obstacles.add(p)
        
    if not found:
        print(f"Failed to route {src_id} to {tgt_id}")
        return []
        
    # Reconstruct path
    curr = (tgx, tgy)
    path = []
    while curr is not None:
        path.append(curr)
        if curr not in came_from: break
        curr = came_from[curr][0]
        
    path.reverse()
    
    # Update usage
    for p in path:
        used_paths[p] = used_paths.get(p, 0) + 1
        
    # Extract corners
    corners = []
    if len(path) > 2:
        for i in range(1, len(path)-1):
            px, py = path[i-1]
            cx, cy = path[i]
            nx, ny = path[i+1]
            if (cx - px) != (nx - cx) or (cy - py) != (ny - cy):
                corners.append((cx * grid_size, cy * grid_size))
                
    return corners

# Process edges
for cell in root_el.findall('mxCell'):
    if cell.attrib.get('edge') == '1':
        src_id = cell.attrib.get('source')
        tgt_id = cell.attrib.get('target')
        if not src_id or not tgt_id: continue
        
        # Calculate center ports for start/end to guide A* nicely
        sc = cells[src_id]
        tc = cells[tgt_id]
        sx = sc['x'] + sc['w']//2
        sy = sc['y'] + sc['h']//2
        tx = tc['x'] + tc['w']//2
        ty = tc['y'] + tc['h']//2
        
        corners = route_edge(sx, sy, tx, ty, src_id, tgt_id)
        
        # Modify cell style to straight lines so our waypoints are followed EXACTLY
        style = cell.attrib.get('style', '')
        style = style.replace('edgeStyle=orthogonalEdgeStyle;', '')
        style = style.replace('edgeStyle=elbowEdgeStyle;', '')
        if 'edgeStyle=orthogonalEdgeStyle' not in style and 'edgeStyle=elbowEdgeStyle' not in style:
             # Just ensure no other edgeStyle is present
             pass
        cell.attrib['style'] = style
        
        if corners:
            geo = cell.find('mxGeometry')
            arr = geo.find('Array')
            if arr is not None:
                geo.remove(arr)
            arr = ET.SubElement(geo, 'Array', {'as': 'points'})
            for cx, cy in corners:
                ET.SubElement(arr, 'mxPoint', {'x': str(cx), 'y': str(cy)})

tree.write('docs/Conceptual-Data-Model.drawio', encoding='utf-8', xml_declaration=True)
print("Scaling and custom routing complete!")
