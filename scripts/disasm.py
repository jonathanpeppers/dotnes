#!/usr/bin/env python3
"""Disassemble a .nes ROM's PRG section (6502)."""
import sys

OPCODES = {
    0x00: ('BRK', 'imp', 1), 0x01: ('ORA', 'inx', 2), 0x05: ('ORA', 'zpg', 2),
    0x06: ('ASL', 'zpg', 2), 0x08: ('PHP', 'imp', 1), 0x09: ('ORA', 'imm', 2),
    0x0A: ('ASL', 'acc', 1), 0x0D: ('ORA', 'abs', 3), 0x0E: ('ASL', 'abs', 3),
    0x10: ('BPL', 'rel', 2), 0x11: ('ORA', 'iny', 2), 0x15: ('ORA', 'zpx', 2),
    0x16: ('ASL', 'zpx', 2), 0x18: ('CLC', 'imp', 1), 0x19: ('ORA', 'aby', 3),
    0x1D: ('ORA', 'abx', 3), 0x1E: ('ASL', 'abx', 3),
    0x20: ('JSR', 'abs', 3), 0x21: ('AND', 'inx', 2), 0x24: ('BIT', 'zpg', 2),
    0x25: ('AND', 'zpg', 2), 0x26: ('ROL', 'zpg', 2), 0x28: ('PLP', 'imp', 1),
    0x29: ('AND', 'imm', 2), 0x2A: ('ROL', 'acc', 1), 0x2C: ('BIT', 'abs', 3),
    0x2D: ('AND', 'abs', 3), 0x2E: ('ROL', 'abs', 3),
    0x30: ('BMI', 'rel', 2), 0x31: ('AND', 'iny', 2), 0x35: ('AND', 'zpx', 2),
    0x36: ('ROL', 'zpx', 2), 0x38: ('SEC', 'imp', 1), 0x39: ('AND', 'aby', 3),
    0x3D: ('AND', 'abx', 3), 0x3E: ('ROL', 'abx', 3),
    0x40: ('RTI', 'imp', 1), 0x41: ('EOR', 'inx', 2), 0x45: ('EOR', 'zpg', 2),
    0x46: ('LSR', 'zpg', 2), 0x48: ('PHA', 'imp', 1), 0x49: ('EOR', 'imm', 2),
    0x4A: ('LSR', 'acc', 1), 0x4C: ('JMP', 'abs', 3), 0x4D: ('EOR', 'abs', 3),
    0x4E: ('LSR', 'abs', 3),
    0x50: ('BVC', 'rel', 2), 0x51: ('EOR', 'iny', 2), 0x55: ('EOR', 'zpx', 2),
    0x56: ('LSR', 'zpx', 2), 0x58: ('CLI', 'imp', 1), 0x59: ('EOR', 'aby', 3),
    0x5D: ('EOR', 'abx', 3), 0x5E: ('LSR', 'abx', 3),
    0x60: ('RTS', 'imp', 1), 0x61: ('ADC', 'inx', 2), 0x65: ('ADC', 'zpg', 2),
    0x66: ('ROR', 'zpg', 2), 0x68: ('PLA', 'imp', 1), 0x69: ('ADC', 'imm', 2),
    0x6A: ('ROR', 'acc', 1), 0x6C: ('JMP', 'ind', 3), 0x6D: ('ADC', 'abs', 3),
    0x6E: ('ROR', 'abs', 3),
    0x70: ('BVS', 'rel', 2), 0x71: ('ADC', 'iny', 2), 0x75: ('ADC', 'zpx', 2),
    0x76: ('ROR', 'zpx', 2), 0x78: ('SEI', 'imp', 1), 0x79: ('ADC', 'aby', 3),
    0x7D: ('ADC', 'abx', 3), 0x7E: ('ROR', 'abx', 3),
    0x81: ('STA', 'inx', 2), 0x84: ('STY', 'zpg', 2), 0x85: ('STA', 'zpg', 2),
    0x86: ('STX', 'zpg', 2), 0x88: ('DEY', 'imp', 1), 0x8A: ('TXA', 'imp', 1),
    0x8C: ('STY', 'abs', 3), 0x8D: ('STA', 'abs', 3), 0x8E: ('STX', 'abs', 3),
    0x90: ('BCC', 'rel', 2), 0x91: ('STA', 'iny', 2), 0x94: ('STY', 'zpx', 2),
    0x95: ('STA', 'zpx', 2), 0x96: ('STX', 'zpy', 2), 0x98: ('TYA', 'imp', 1),
    0x99: ('STA', 'aby', 3), 0x9A: ('TXS', 'imp', 1), 0x9D: ('STA', 'abx', 3),
    0xA0: ('LDY', 'imm', 2), 0xA1: ('LDA', 'inx', 2), 0xA2: ('LDX', 'imm', 2),
    0xA4: ('LDY', 'zpg', 2), 0xA5: ('LDA', 'zpg', 2), 0xA6: ('LDX', 'zpg', 2),
    0xA8: ('TAY', 'imp', 1), 0xA9: ('LDA', 'imm', 2), 0xAA: ('TAX', 'imp', 1),
    0xAC: ('LDY', 'abs', 3), 0xAD: ('LDA', 'abs', 3), 0xAE: ('LDX', 'abs', 3),
    0xB0: ('BCS', 'rel', 2), 0xB1: ('LDA', 'iny', 2), 0xB4: ('LDY', 'zpx', 2),
    0xB5: ('LDA', 'zpx', 2), 0xB6: ('LDX', 'zpy', 2), 0xB8: ('CLV', 'imp', 1),
    0xB9: ('LDA', 'aby', 3), 0xBA: ('TSX', 'imp', 1), 0xBC: ('LDY', 'abx', 3),
    0xBD: ('LDA', 'abx', 3), 0xBE: ('LDX', 'aby', 3),
    0xC0: ('CPY', 'imm', 2), 0xC1: ('CMP', 'inx', 2), 0xC4: ('CPY', 'zpg', 2),
    0xC5: ('CMP', 'zpg', 2), 0xC6: ('DEC', 'zpg', 2), 0xC8: ('INY', 'imp', 1),
    0xC9: ('CMP', 'imm', 2), 0xCA: ('DEX', 'imp', 1), 0xCC: ('CPY', 'abs', 3),
    0xCD: ('CMP', 'abs', 3), 0xCE: ('DEC', 'abs', 3),
    0xD0: ('BNE', 'rel', 2), 0xD1: ('CMP', 'iny', 2), 0xD5: ('CMP', 'zpx', 2),
    0xD6: ('DEC', 'zpx', 2), 0xD8: ('CLD', 'imp', 1), 0xD9: ('CMP', 'aby', 3),
    0xDD: ('CMP', 'abx', 3), 0xDE: ('DEC', 'abx', 3),
    0xE0: ('CPX', 'imm', 2), 0xE1: ('SBC', 'inx', 2), 0xE4: ('CPX', 'zpg', 2),
    0xE5: ('SBC', 'zpg', 2), 0xE6: ('INC', 'zpg', 2), 0xE8: ('INX', 'imp', 1),
    0xE9: ('SBC', 'imm', 2), 0xEA: ('NOP', 'imp', 1), 0xEC: ('CPX', 'abs', 3),
    0xED: ('SBC', 'abs', 3), 0xEE: ('INC', 'abs', 3),
    0xF0: ('BEQ', 'rel', 2), 0xF1: ('SBC', 'iny', 2), 0xF5: ('SBC', 'zpx', 2),
    0xF6: ('INC', 'zpx', 2), 0xF8: ('SED', 'imp', 1), 0xF9: ('SBC', 'aby', 3),
    0xFD: ('SBC', 'abx', 3), 0xFE: ('INC', 'abx', 3),
}

def fmt_operand(mode, val, addr):
    if mode == 'imp' or mode == 'acc':
        return ''
    if mode == 'imm':
        return ' #$%02X' % val
    if mode == 'zpg':
        return ' $%02X' % val
    if mode == 'zpx':
        return ' $%02X,X' % val
    if mode == 'zpy':
        return ' $%02X,Y' % val
    if mode == 'abs':
        return ' $%04X' % val
    if mode == 'abx':
        return ' $%04X,X' % val
    if mode == 'aby':
        return ' $%04X,Y' % val
    if mode == 'ind':
        return ' ($%04X)' % val
    if mode == 'inx':
        return ' ($%02X,X)' % val
    if mode == 'iny':
        return ' ($%02X),Y' % val
    if mode == 'rel':
        signed = val if val < 128 else val - 256
        target = addr + 2 + signed
        return ' $%04X' % target
    return ' ???'

def disasm(data, base, start=None, end=None):
    if start is None:
        start = base
    if end is None:
        end = base + len(data)
    i = start - base
    stop = end - base
    while i < stop and i < len(data):
        addr = base + i
        op = data[i]
        if op in OPCODES:
            name, mode, size = OPCODES[op]
            raw = data[i:i+size]
            hexstr = ' '.join('%02X' % b for b in raw)
            if size == 1:
                val = 0
            elif size == 2:
                val = data[i+1]
            else:
                val = data[i+1] | (data[i+2] << 8)
            operand = fmt_operand(mode, val, addr)
            print('$%04X: %-9s %s%s' % (addr, hexstr, name, operand))
            i += size
        else:
            print('$%04X: %02X        .byte $%02X' % (addr, data[i], data[i]))
            i += 1

def main():
    if len(sys.argv) < 2:
        print('Usage: python disasm.py <file.nes> [start_hex] [end_hex]')
        print('  start/end are NES addresses like 8500 (default: 8000-FFFF)')
        sys.exit(1)

    rom = open(sys.argv[1], 'rb').read()
    # iNES header is 16 bytes, PRG ROM follows
    prg = rom[16:16+0x8000]

    start = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x8000
    end = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x8000 + len(prg)

    disasm(prg, 0x8000, start, end)

if __name__ == '__main__':
    main()
