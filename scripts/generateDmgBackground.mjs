import { PNG } from 'pngjs';
import fs from 'fs';

const width = 540;
const height = 380;

const png = new PNG({ width, height });

// Create a beautiful dark gradient background (Furrycord theme)
for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
        const idx = (width * y + x) << 2;
        
        // Dark gradient from top-left to bottom-right
        const gradientX = x / width;
        const gradientY = y / height;
        
        // Dark purple/blue gradient
        const r = Math.floor(20 + gradientX * 15);
        const g = Math.floor(15 + gradientY * 20);
        const b = Math.floor(35 + (gradientX + gradientY) * 25);
        
        png.data[idx] = r;     // Red
        png.data[idx + 1] = g; // Green
        png.data[idx + 2] = b; // Blue
        png.data[idx + 3] = 255; // Alpha
    }
}

// Add subtle accent line at the bottom
const accentY = height - 60;
for (let x = 0; x < width; x++) {
    for (let y = accentY; y < accentY + 3; y++) {
        const idx = (width * y + x) << 2;
        const gradient = x / width;
        png.data[idx] = Math.floor(100 + gradient * 50);     // Red
        png.data[idx + 1] = Math.floor(80 + gradient * 40); // Green
        png.data[idx + 2] = Math.floor(200 + gradient * 55); // Blue
        png.data[idx + 3] = 255;
    }
}

const buffer = PNG.sync.write(png);
fs.writeFileSync('build/dmg-background.png', buffer);

console.log('DMG background image created successfully!');
