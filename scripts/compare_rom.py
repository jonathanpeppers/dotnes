"""
Compare two NES ROMs side-by-side with 6502 disassembly.
Usage: python scripts/compare_rom.py <cc65.nes> <ours.nes>
"""
import sys

oplen = [1]*256
for op in [0x09,0x29,0x49,0x69,0xA0,0xA2,0xA9,0xC0,0xC9,0xE0,0xE9]: oplen[op] = 2
for op in [0x05,0x06,0x24,0x25,0x26,0x45,0x46,0x65,0x66,0x84,0x85,0x86,0xA4,0xA5,0xA6,0xC4,0xC5,0xC6,0xE4,0xE5,0xE6]: oplen[op] = 2
for op in [0x0D,0x0E,0x20,0x2C,0x2D,0x2E,0x4C,0x4D,0x4E,0x6D,0x6E,0x8C,0x8D,0x8E,0xAC,0xAD,0xAE,0xCC,0xCD,0xCE,0xEC,0xED,0xEE]: oplen[op] = 3
for op in [0x10,0x30,0x50,0x70,0x90,0xB0,0xD0,0xF0]: oplen[op] = 2
for op in [0x91,0xB1,0x11,0x31,0x51,0x71,0xD1,0xF1]: oplen[op] = 2
for op in [0x1D,0x19,0x3D,0x39,0x5D,0x59,0x7D,0x79,0x9D,0x99,0xBD,0xB9,0xBC,0xBE,0xDD,0xD9,0xFD,0xF9]: oplen[op] = 3
for op in [0x15,0x16,0x35,0x36,0x55,0x56,0x75,0x76,0x94,0x95,0x96,0xB4,0xB5,0xB6,0xD5,0xD6,0xF5,0xF6]: oplen[op] = 2
oplen[0x6C] = 3

names = {
    0x00:'BRK',0x08:'PHP',0x0A:'ASL',0x18:'CLC',0x28:'PLP',0x2A:'ROL',0x38:'SEC',
    0x40:'RTI',0x48:'PHA',0x4A:'LSR',0x58:'CLI',0x60:'RTS',0x68:'PLA',0x6A:'ROR',
    0x78:'SEI',0x88:'DEY',0x8A:'TXA',0x98:'TYA',0x9A:'TXS',0xA8:'TAY',0xAA:'TAX',
    0xB8:'CLV',0xBA:'TSX',0xC8:'INY',0xCA:'DEX',0xD8:'CLD',0xE8:'INX',0xEA:'NOP',0xF8:'SED',
    0x20:'JSR',0x4C:'JMP',0x6C:'JMP_i',
    0xA9:'LDA#',0xA2:'LDX#',0xA0:'LDY#',0xC9:'CMP#',0xC0:'CPY#',0xE0:'CPX#',
    0x09:'ORA#',0x29:'AND#',0x49:'EOR#',0x69:'ADC#',0xE9:'SBC#',
    0x85:'STA_z',0x86:'STX_z',0x84:'STY_z',0xA5:'LDA_z',0xA6:'LDX_z',0xA4:'LDY_z',
    0x24:'BIT_z',0xC5:'CMP_z',0xC4:'CPY_z',0xE4:'CPX_z',0xC6:'DEC_z',0xE6:'INC_z',
    0x05:'ORA_z',0x25:'AND_z',0x45:'EOR_z',0x65:'ADC_z',0xE5:'SBC_z',
    0x06:'ASL_z',0x26:'ROL_z',0x46:'LSR_z',0x66:'ROR_z',
    0x8D:'STA',0x8E:'STX',0x8C:'STY',0xAD:'LDA',0xAE:'LDX',0xAC:'LDY',
    0x2C:'BIT',0xCD:'CMP',0xCC:'CPY',0xEC:'CPX',0xCE:'DEC',0xEE:'INC',
    0x0D:'ORA',0x2D:'AND',0x4D:'EOR',0x6D:'ADC',0xED:'SBC',
    0x0E:'ASL',0x2E:'ROL',0x4E:'LSR',0x6E:'ROR',
    0x10:'BPL',0x30:'BMI',0x50:'BVC',0x70:'BVS',0x90:'BCC',0xB0:'BCS',0xD0:'BNE',0xF0:'BEQ',
    0x91:'STA(y)',0xB1:'LDA(y)',0x11:'ORA(y)',0x31:'AND(y)',0x51:'EOR(y)',
    0x71:'ADC(y)',0xD1:'CMP(y)',0xF1:'SBC(y)',
    0x95:'STA_zx',0xB5:'LDA_zx',0x15:'ORA_zx',0x35:'AND_zx',0x55:'EOR_zx',
    0x75:'ADC_zx',0xD5:'CMP_zx',0xF5:'SBC_zx',0x94:'STY_zx',0xB4:'LDY_zx',
    0x16:'ASL_zx',0x36:'ROL_zx',0x56:'LSR_zx',0x76:'ROR_zx',0xD6:'DEC_zx',0xF6:'INC_zx',
    0x96:'STX_zy',0xB6:'LDX_zy',
    0x9D:'STA_x',0xBD:'LDA_x',0x1D:'ORA_x',0x3D:'AND_x',0x5D:'EOR_x',
    0x7D:'ADC_x',0xDD:'CMP_x',0xFD:'SBC_x',0xBC:'LDY_x',
    0x99:'STA_y',0xB9:'LDA_y',0x19:'ORA_y',0x39:'AND_y',0x59:'EOR_y',
    0x79:'ADC_y',0xD9:'CMP_y',0xF9:'SBC_y',0xBE:'LDX_y',
}

def disasm(rom, start_file, count):
    lines = []
    i = start_file
    end = min(start_file + count, len(rom))
    while i < end:
        op = rom[i]
        n = names.get(op, '?%02X' % op)
        length = oplen[op]
        nes = 0x8000 + (i - 16)
        if i + length > end:
            break
        if length == 3:
            val = rom[i+1] | (rom[i+2] << 8)
            lines.append((nes, '%s $%04X' % (n, val)))
        elif length == 2:
            lines.append((nes, '%s $%02X' % (n, rom[i+1])))
        else:
            lines.append((nes, n))
        i += length
    return lines

def normalize(inst_text):
    """Strip absolute addresses for semantic comparison."""
    parts = inst_text.split()
    if len(parts) == 2 and parts[1].startswith('$') and len(parts[1]) == 5:
        if parts[0] in ('JSR','JMP','STA','STX','STY','LDA','LDX','LDY',
                         'INC','DEC','CMP','BIT','ORA','AND','EOR','ADC','SBC',
                         'ASL','ROL','LSR','ROR','JMP_i','STA_x','LDA_x','STA_y','LDA_y'):
            return parts[0] + ' $????'
    return inst_text

def main():
    if len(sys.argv) < 3:
        print('Usage: python scripts/compare_rom.py <cc65.nes> <dotnes.nes>')
        print('  Compares two NES ROMs with byte-level and instruction-level analysis.')
        sys.exit(1)

    cc65 = open(sys.argv[1],'rb').read()
    ours = open(sys.argv[2],'rb').read()

    print('File sizes: %s=%d, %s=%d' % (sys.argv[1], len(cc65), sys.argv[2], len(ours)))

    # Byte-level diff summary
    diffs = [(i, cc65[i], ours[i]) for i in range(min(len(cc65),len(ours))) if cc65[i] != ours[i]]
    hdr  = [d for d in diffs if d[0] < 16]
    prg  = [d for d in diffs if 16 <= d[0] < 16+32768]
    chrr = [d for d in diffs if d[0] >= 16+32768]
    print('Total byte diffs: %d (header=%d, PRG=%d, CHR=%d)' % (len(diffs), len(hdr), len(prg), len(chrr)))

    # Group contiguous diffs
    if diffs:
        groups = []
        current = [diffs[0]]
        for d in diffs[1:]:
            if d[0] - current[-1][0] <= 4:
                current.append(d)
            else:
                groups.append(current)
                current = [d]
        groups.append(current)
        print()
        print('Diff groups:')
        for g in groups:
            s, e = g[0][0], g[-1][0]
            nes_s = 0x8000 + (s - 16)
            print('  0x%04X (NES $%04X): %d diffs, %d byte span' % (s, nes_s, len(g), e-s+1))

    # Find string data
    needle = bytes([0x4C, 0x4F, 0x4C, 0x21])  # "LOL!"
    for label, rom in [('cc65', cc65), ('ours', ours)]:
        idx, count = 16, 0
        while True:
            idx = rom.find(needle, idx)
            if idx < 0: break
            count += 1
            idx += 30
        if count > 0:
            first = rom.find(needle, 16)
            nes_addr = 0x8000 + (first - 16)
            print('%s: %d string copies, first at NES $%04X' % (label, count, nes_addr))

    # Disassemble main code region (largest diff group)
    if prg:
        largest = max(groups, key=lambda g: len(g))
        start_file = largest[0][0]
        span = largest[-1][0] - start_file + 50  # extra context
        
        cc65_main = disasm(cc65, start_file, span)
        ours_main = disasm(ours, start_file, span)

        print()
        print('%-30s | %-30s | Match?' % ('cc65', 'ours'))
        print('-' * 75)
        ci, oi, mismatches = 0, 0, 0
        while ci < len(cc65_main) and oi < len(ours_main):
            c = cc65_main[ci]
            o = ours_main[oi]
            cn = normalize(c[1])
            on = normalize(o[1])
            match = 'OK' if cn == on else '** DIFF **'
            if cn != on:
                mismatches += 1
            print('%04X %-25s | %04X %-25s | %s' % (c[0], c[1], o[0], o[1], match))
            ci += 1
            oi += 1
        
        print()
        print('Instruction mismatches (ignoring addresses): %d' % mismatches)

if __name__ == '__main__':
    main()
