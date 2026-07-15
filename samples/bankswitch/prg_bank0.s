.segment "CODE"
.import ppu_on_all
.export bank0_entry

bank0_entry:
    jsr ppu_on_all
    lda #<bank0_data
    ldx #>bank0_data
    rts

.segment "RODATA"
bank0_data:
    .byte $4D,$4D,$43,$33
bank0_data_pointer:
    .word bank0_data
