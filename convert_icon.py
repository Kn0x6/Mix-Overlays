#!/usr/bin/env python3
import sys
from PIL import Image

def convert_to_ico(input_path, output_path):
    try:
        # Ouvrir l'image JPEG
        img = Image.open(input_path)
        
        # Redimensionner si nécessaire (les icônes Windows utilisent généralement 256x256)
        if img.size != (256, 256):
            img = img.resize((256, 256), Image.Resampling.LANCZOS)
        
        # Convertir en mode RGB si nécessaire
        if img.mode != 'RGB':
            img = img.convert('RGB')
        
        # Sauvegarder en format ICO
        img.save(output_path, format='ICO', sizes=[(256, 256)])
        print(f"Conversion réussie : {input_path} -> {output_path}")
        return True
        
    except Exception as e:
        print(f"Erreur lors de la conversion : {e}")
        return False

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python convert_icon.py input.jpg output.ico")
        sys.exit(1)
    
    input_file = sys.argv[1]
    output_file = sys.argv[2]
    
    success = convert_to_ico(input_file, output_file)
    sys.exit(0 if success else 1)