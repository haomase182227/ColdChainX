import os
import re
import glob

context_path = 'ColdChainX.Infrastructure/Persistence/ApplicationDbContext.cs'
with open(context_path, 'r', encoding='utf-8') as f:
    context_code = f.read()

# Pattern: entity.HasKey(e => e.AlertId) -> we need the class name too.
entities_pks = {}
blocks = re.findall(r'modelBuilder\.Entity<(\w+)>\(entity =>\s*\{.*?(?=\s*\}\);)', context_code, re.DOTALL)
for entity_name in re.findall(r'modelBuilder\.Entity<(\w+)>', context_code):
    # Find the block for this entity
    block_match = re.search(rf'modelBuilder\.Entity<{entity_name}>\((?:entity|e) =>\s*\{{(.*?)\}}\);', context_code, re.DOTALL)
    if block_match:
        block = block_match.group(1)
        pk_match = re.search(r'\.HasKey\((?:entity|e) => (?:entity|e)\.(\w+)\)', block)
        if pk_match:
            entities_pks[entity_name] = pk_match.group(1)

print('Better PKs:', len(entities_pks), entities_pks)

# We update DbContext by splitting by modelBuilder.Entity and replacing
def replace_in_block(match):
    entity_name = match.group(1)
    block = match.group(2)
    pk_name = entities_pks.get(entity_name)
    if pk_name and pk_name != 'Id':
        # replace e.PkName to e.Id
        block = re.sub(rf'\b([a-zA-Z_]\w*\.){pk_name}\b', r'\g<1>Id', block)
    # also remove .HasMaxLength(...) for Guids
    block = re.sub(r'\.HasMaxLength\(\d+\)', '', block)
    return f'modelBuilder.Entity<{entity_name}>({block}'

new_context_code = re.sub(r'modelBuilder\.Entity<(\w+)>\((.*?)(?=\n\s*modelBuilder\.Entity|\n\s*OnModelCreatingPartial|\z)', replace_in_block, context_code, flags=re.DOTALL)

with open(context_path, 'w', encoding='utf-8') as f:
    f.write(new_context_code)


entities_files = glob.glob('ColdChainX.Core/Entities/*.cs')
for filepath in entities_files:
    filename = os.path.basename(filepath)
    entity_name = filename.replace('.cs', '')
    
    with open(filepath, 'r', encoding='utf-8') as f:
        code = f.read()
    
    # Replace all string/string? <Word>Id to Guid/Guid?
    # Make sure to catch properties ending with Id
    code = re.sub(r'\bstring\s+(\w+Id)\b', r'Guid \1', code)
    code = re.sub(r'\bstring\?\s+(\w+Id)\b', r'Guid? \1', code)
    # Remove = null!
    code = re.sub(r'Guid(\??\s+\w+Id\s*\{\s*get;\s*set;\s*\})\s*=\s*null!;', r'Guid\g<1>', code)

    pk_name = entities_pks.get(entity_name)
    if pk_name and pk_name != 'Id':
        code = re.sub(rf'\bGuid\s+{pk_name}\b', 'Guid Id', code)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(code)

print("Done python script 2")
