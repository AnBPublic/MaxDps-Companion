//! Cell-grid builder: turns one captured BGRA bitmap into the per-cell colours
//! the protocol decoder consumes.
//!
//! Port of `app/MaxDpsCompanion/ScreenSampler.cs` (read-only reference). For a
//! cell of `MEDIAN_MIN_CELL_SIZE` pixels or larger the value is the **5-tap
//! median** (centre plus the 4-neighbourhood, clamped inside the cell) — the
//! Aethys-proven filter that kills HDR dither and tolerates 1px misalignment.
//! For 1px cells the median would bleed into neighbours, so the exact centre
//! pixel is used instead.
//!
//! The platform `capture` backend already performed the BitBlt; this module is
//! pure arithmetic over the returned [`Frame`], so it has no I/O and no unsafe.

use mdc_platform::Frame;
use mdc_protocol::Color;

/// Taps sampled per cell at [`MEDIAN_MIN_CELL_SIZE`]+. Must stay odd.
pub const TAPS_PER_CELL: usize = 5;

/// Cell size at/above which the 5-tap median is used; below it the centre pixel
/// is read (`ScreenSampler.MedianMinCellSize`).
pub const MEDIAN_MIN_CELL_SIZE: i32 = 2;

/// Builds one [`Color`] per cell (`cell_count` cells, each `cell_size` px wide).
///
/// `frame` is the captured strip region (`cell_size * cell_count` wide and
/// `cell_size` tall). Reads outside the frame degrade to black rather than
/// panicking.
pub fn sample_cells(frame: &Frame, cell_size: u32, cell_count: usize) -> Vec<Color> {
    (0..cell_count)
        .map(|cell| sample_cell(frame, cell, cell_size))
        .collect()
}

fn sample_cell(frame: &Frame, cell: usize, cell_size: u32) -> Color {
    let size = cell_size as i32;
    if size < MEDIAN_MIN_CELL_SIZE {
        return read(frame, cell as i32 * size + size / 2, size / 2);
    }

    let start = cell as i32 * size;
    let cx = start + size / 2;
    let cy = size / 2;
    let left = start.max(cx - 1);
    let right = (start + size - 1).min(cx + 1);
    let up = (cy - 1).max(0);
    let down = (cy + 1).min(size - 1);

    let taps = [
        read(frame, cx, cy),
        read(frame, left, cy),
        read(frame, right, cy),
        read(frame, cx, up),
        read(frame, cx, down),
    ];
    Color::new(
        median5([taps[0].r, taps[1].r, taps[2].r, taps[3].r, taps[4].r]),
        median5([taps[0].g, taps[1].g, taps[2].g, taps[3].g, taps[4].g]),
        median5([taps[0].b, taps[1].b, taps[2].b, taps[3].b, taps[4].b]),
    )
}

fn median5(mut values: [u8; TAPS_PER_CELL]) -> u8 {
    values.sort_unstable();
    values[TAPS_PER_CELL / 2]
}

fn read(frame: &Frame, x: i32, y: i32) -> Color {
    if x < 0 || y < 0 {
        return Color::new(0, 0, 0);
    }
    match frame.pixel(x as u32, y as u32) {
        Some([r, g, b]) => Color::new(r, g, b),
        None => Color::new(0, 0, 0),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn solid_frame(cell_size: u32, count: usize, color: [u8; 3]) -> Frame {
        let width = cell_size * count as u32;
        let height = cell_size;
        let mut pixels = vec![0u8; (width * height * 4) as usize];
        for cell in 0..count {
            set_cell(&mut pixels, width, cell_size, cell, color);
        }
        Frame { width, height, pixels }
    }

    fn set_cell(pixels: &mut [u8], width: u32, cell_size: u32, cell: usize, color: [u8; 3]) {
        let x0 = cell as u32 * cell_size;
        for y in 0..cell_size {
            for x in x0..x0 + cell_size {
                let i = ((y * width + x) * 4) as usize;
                pixels[i] = color[2];
                pixels[i + 1] = color[1];
                pixels[i + 2] = color[0];
                pixels[i + 3] = 255;
            }
        }
    }

    fn set_pixel(pixels: &mut [u8], width: u32, x: u32, y: u32, color: [u8; 3]) {
        let i = ((y * width + x) * 4) as usize;
        pixels[i] = color[2];
        pixels[i + 1] = color[1];
        pixels[i + 2] = color[0];
        pixels[i + 3] = 255;
    }

    #[test]
    fn uniform_cells_median_to_the_block_colour() {
        let frame = solid_frame(8, 2, [10, 20, 30]);
        assert_eq!(sample_cells(&frame, 8, 2), vec![Color::new(10, 20, 30); 2]);
    }

    #[test]
    fn median_rejects_a_single_noise_tap() {
        let mut frame = solid_frame(8, 1, [10, 20, 30]);
        // Cell 0: centre tap is (4,4); the left tap reads (3,4).
        set_pixel(&mut frame.pixels, frame.width, 3, 4, [200, 200, 200]);
        assert_eq!(sample_cells(&frame, 8, 1), vec![Color::new(10, 20, 30)]);
    }

    #[test]
    fn one_pixel_cells_use_the_exact_centre() {
        let frame = solid_frame(1, 3, [1, 2, 3]);
        assert_eq!(sample_cells(&frame, 1, 3), vec![Color::new(1, 2, 3); 3]);
    }

    #[test]
    fn out_of_bounds_reads_degrade_to_black() {
        let frame = solid_frame(8, 1, [10, 20, 30]);
        // Asking for 4 cells when only 1 was captured yields black for the rest.
        let cells = sample_cells(&frame, 8, 4);
        assert_eq!(cells[0], Color::new(10, 20, 30));
        assert_eq!(cells[3], Color::new(0, 0, 0));
    }
}
