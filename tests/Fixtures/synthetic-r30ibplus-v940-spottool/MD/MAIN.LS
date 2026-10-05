/PROG  MAIN
/ATTR
OWNER		= MNEDITOR;
COMMENT		= "Main - SYNTHETIC";
PROG_SIZE	= 1234;
CREATE		= DATE 26-01-15  TIME 08:12:30;
MODIFIED	= DATE 26-09-30  TIME 14:02:11;
FILE_NAME	= ;
VERSION		= 0;
LINE_COUNT	= 7;
MEMORY_SIZE	= 1590;
PROTECT		= READ_WRITE;
DEFAULT_GROUP	= 1,*,*,*,*;
CONTROL_CODE	= 00000000 00000000;
/APPL
/MN
   1:  UFRAME_NUM=1 ;
   2:  UTOOL_NUM=1 ;
   3:J P[1] 100% FINE    ;
   4:L P[2] 2000mm/sec CNT100    ;
   5:  CALL WELD_A    ;
   6:J P[1] 100% FINE    ;
   7:  R[2:Weld count]=R[2:Weld count]+1    ;
/POS
P[1]{
   GP1:
	UF : 1, UT : 1,		CONFIG : 'N U T, 0, 0, 0',
	X =  1500.000  mm,	Y =     0.000  mm,	Z =  1200.000  mm,
	W =   180.000 deg,	P =     0.000 deg,	R =     0.000 deg
};
P[2]{
   GP1:
	UF : 1, UT : 1,		CONFIG : 'N U T, 0, 0, 0',
	X =  1650.000  mm,	Y =   250.000  mm,	Z =   900.000  mm,
	W =   180.000 deg,	P =     0.000 deg,	R =    90.000 deg
};
/END
