import os
import re
import glob

# 1. Parse DbContext to find PKs
context_path = 'ColdChainX.Infrastructure/Persistence/ApplicationDbContext.cs'
with open(context_path, 'r', encoding='utf-8') as f:
    context_code = f.read()

# Pattern: entity.HasKey(e => e.AlertId) -> we need the class name too.
# Actually modelBuilder.Entity<AlertLog>(entity => ... entity.HasKey(e => e.AlertId)
entities_pks = {}
entity_blocks = re.findall(r'modelBuilder\.Entity<(\w+)>\((.*?)\n\s+modelBuilder\.', context_code + '\n        modelBuilder.', re.DOTALL)
for entity_name, block in entity_blocks:
    pk_match = re.search(r'entity\.HasKey\(\w+ => \w+\.(\w+)\)', block)
    if pk_match:
        entities_pks[entity_name] = pk_match.group(1)

print('PKs:', entities_pks)

# 2. Update DbContext
new_context_code = context_code
for entity_name, pk_name in entities_pks.items():
    # In DbContext, replace e.PkName with e.Id ONLY for that entity block
    # It's tricky to do block replacement. Let's just global replace for simplicity?
    pass

# We will do block replacement for DbContext
def replace_in_block(match):
    entity_name = match.group(1)
    block = match.group(2)
    pk_name = entities_pks.get(entity_name)
    if pk_name and pk_name != 'Id':
        # replace e.PkName to e.Id
        block = re.sub(rf'\b([a-zA-Z_]\w*\.){pk_name}\b', r'\g<1>Id', block)
    # also remove .HasMaxLength(...) for Guids
    # this is simplistic but mostly works
    return f'modelBuilder.Entity<{entity_name}>({block}'

new_context_code = re.sub(r'modelBuilder\.Entity<(\w+)>\((.*?)(?=\n\s+modelBuilder\.Entity|\n\s+OnModelCreatingPartial)', replace_in_block, new_context_code, flags=re.DOTALL)

with open(context_path, 'w', encoding='utf-8') as f:
    f.write(new_context_code)


# 3. Update Entities
entities_files = glob.glob('ColdChainX.Core/Entities/*.cs')
for filepath in entities_files:
    filename = os.path.basename(filepath)
    entity_name = filename.replace('.cs', '')
    
    with open(filepath, 'r', encoding='utf-8') as f:
        code = f.read()
    
    # Replace all string/string? <Word>Id to Guid/Guid?
    code = re.sub(r'\bstring\s+(\w+Id)\b', r'Guid \1', code)
    code = re.sub(r'\bstring\?\s+(\w+Id)\b', r'Guid? \1', code)
    # Removing = null! from Guid
    code = re.sub(r'Guid(\??\s+\w+Id\s*\{\s*get;\s*set;\s*\})\s*=\s*null!;', r'Guid\1', code)

    # Rename the Primary Key property to Id
    pk_name = entities_pks.get(entity_name)
    if pk_name and pk_name != 'Id':
        code = re.sub(rf'\bGuid\s+{pk_name}\b', 'Guid Id', code)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(code)

print("Done python script")
