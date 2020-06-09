`timescale 1 ps / 1 ps

module top
(
	input wire CLK,
	
	input wire SPI_SCLK,
	input wire SPI_NSS,
	input wire SPI_MOSI,
	output wire SPI_MISO,
	
	output wire [7:0] LED,
	
	inout wire [7:0] ADDA,
	output wire ADDA_CLK,
	
	output wire IFCLK,
	inout wire [7:0] FD,
	output wire SLRDN,
	output wire SLWRN,
	input wire [2:0] FLAGN,
	output wire SLOEN,
	output wire [1:0] FIFOADR,
	output wire PKTENDN
);



	wire [31:0] pio0;
	wire [31:0] pio1;
	wire [31:0] pio2;
	wire [31:0] pio3;
	wire [31:0] pio4;
	
	pll	pll_inst (
		.inclk0 (CLK),
		.c0 (IFCLK) // 48M
	);
	
	QsysCore u0 (
		.clk_clk                                                                                         (IFCLK),
		.reset_reset_n                                                                                   (1'b1),
		.spi_slave_to_avalon_mm_master_bridge_0_export_0_mosi_to_the_spislave_inst_for_spichain          (SPI_MOSI),
		.spi_slave_to_avalon_mm_master_bridge_0_export_0_nss_to_the_spislave_inst_for_spichain           (SPI_NSS),
		.spi_slave_to_avalon_mm_master_bridge_0_export_0_miso_to_and_from_the_spislave_inst_for_spichain (SPI_MISO),
		.spi_slave_to_avalon_mm_master_bridge_0_export_0_sclk_to_the_spislave_inst_for_spichain          (SPI_SCLK),
		.pio_0_external_connection_export                                                                (pio0),
		.pio_1_external_connection_export                                                                (pio1),
		.pio_2_external_connection_export                                                                (pio2),
		.pio_3_external_connection_export                                                                (pio3),
		.pio_4_external_connection_export                                                                (pio4)
	);
	
	assign LED = pio0[7:0];



	reg [7:0] uadda_r;
	wire signed [7:0] adda;
	
	always @(posedge IFCLK) begin
		uadda_r <= ADDA;
	end
	assign ADDA = 8'bzzzzzzzz;
	assign ADDA_CLK = IFCLK;
	assign adda = (uadda_r[7] == 0) ? uadda_r + 8'h80 : uadda_r - 8'h80;



	localparam CIC_WIDTH = 24;
	
	wire signed [11:0] sin;
	wire signed [11:0] cos;
	
	reg signed [CIC_WIDTH-1:0] i;
	reg signed [CIC_WIDTH-1:0] q;
	
	MyNCO #(
		.OUT_WIDTH(12)
	) nco_inst (
		.clk       (IFCLK),
		.reset_n   (1'b1),
		.clken     (1'b1),
		.phi_inc_i (pio1),
		.fsin_o    (sin),
		.fcos_o    (cos),
		.out_valid ()
	);
	
	always @(posedge IFCLK) begin
		i <= adda * cos;
		q <= adda * -sin;
	end



	localparam CIC_NUM_STAGES = 4;
	localparam CIC_MAX_RATE = 160;
	
	wire [3:0] cic_rate_div;
	wire [4:0] cic_out_gain;
	
	wire signed [CIC_WIDTH-1:0] icic;
	wire signed [CIC_WIDTH-1:0] qcic;
	wire icic_valid;
	
	assign cic_rate_div = pio3[3:0]; // 0:37.5k, 1:75k, 2:150k, 3:300k, 4:600k, 5:1.2M
	assign cic_out_gain = pio4[4:0];
	
	MyCIC #(
		.NUM_STAGES(CIC_NUM_STAGES),
		.MAX_RATE(CIC_MAX_RATE),
		.DATA_WIDTH(CIC_WIDTH)
	) cic_inst_i (
		.clk       (IFCLK),
		.reset_n   (1'b1),
		.rate      (CIC_MAX_RATE >> cic_rate_div),
		.rate_div  (cic_rate_div),
		.gain      (cic_out_gain),
		.in_error  (2'b00),
		.in_valid  (1'b1),
		.in_ready  (),
		.in_data   (i),
		.out_data  (icic),
		.out_error (),
		.out_valid (icic_valid),
		.out_ready (1'b1)
	);
	
	MyCIC #(
		.NUM_STAGES(CIC_NUM_STAGES),
		.MAX_RATE(CIC_MAX_RATE),
		.DATA_WIDTH(CIC_WIDTH)
	) cic_inst_q (
		.clk       (IFCLK),
		.reset_n   (1'b1),
		.rate      (CIC_MAX_RATE >> cic_rate_div),
		.rate_div  (cic_rate_div),
		.gain      (cic_out_gain),
		.in_error  (2'b00),
		.in_valid  (1'b1),
		.in_ready  (),
		.in_data   (q),
		.out_data  (qcic),
		.out_error (),
		.out_valid (),
		.out_ready (1'b1)
	);



	localparam FIR_WIDTH = 16;
	
	wire [4:0] fir_out_gain;
	
	wire signed [FIR_WIDTH-1:0] ifir;
	wire signed [FIR_WIDTH-1:0] qfir;
	wire ifir_valid;
	
	assign fir_out_gain = 0;
	
	MyFIR #(
		.DATA_WIDTH(FIR_WIDTH)
	) fir8_inst_i (
		.clk       (IFCLK),
		.reset_n   (1'b1),
		.gain      (fir_out_gain),
		.ast_sink_data (icic[CIC_WIDTH-1 -: FIR_WIDTH]),
		.ast_sink_valid (icic_valid),
		.ast_sink_error (2'b00),
		.ast_source_data (ifir),
		.ast_source_valid (ifir_valid),
		.ast_source_error ()
	);
 	
	MyFIR #(
		.DATA_WIDTH(FIR_WIDTH)
	) fir8_inst_q (
		.clk       (IFCLK),
		.reset_n   (1'b1),
		.gain      (fir_out_gain),
		.ast_sink_data (qcic[CIC_WIDTH-1 -: FIR_WIDTH]),
		.ast_sink_valid (icic_valid),
		.ast_sink_error (2'b00),
		.ast_source_data (qfir),
		.ast_source_valid (),
		.ast_source_error ()
	);



	localparam IDLE = 3'd0;
	localparam BYTE0 = 3'd1;
	localparam BYTE1 = 3'd2;
	localparam BYTE2 = 3'd3;
	localparam BYTE3 = 3'd4;
	
	wire iq_swap;
	wire [31:0] fifo_data;	
	wire fifo_valid;
	wire fifo_ready;	
	reg [31:0] fifo_data_r;	
	reg [2:0] cur;
	reg [7:0] fd_r;
	
	assign iq_swap = pio2[0];
	assign fifo_data = iq_swap ? { ifir, qfir } : { qfir, ifir };
	assign fifo_valid = ifir_valid;
	
	assign SLRDN = 1'b1;
	assign SLOEN = 1'b1;
	assign FIFOADR = 2'b00;
	assign PKTENDN = 1'b1;
	
	always @(negedge IFCLK) begin
		case (cur)
			IDLE:
				if (fifo_valid & fifo_ready) begin
					cur <= BYTE0;
					fd_r <= fifo_data[7:0];
					fifo_data_r <= fifo_data;
				end else begin
					cur <= IDLE;
				end
			BYTE0:
				begin
					cur <= BYTE1;
					fd_r <= fifo_data_r[15:8];
				end
			BYTE1:
				begin
					cur <= BYTE2;
					fd_r <= fifo_data_r[23:16];
				end
			BYTE2:
				begin
					cur <= BYTE3;
					fd_r <= fifo_data_r[31:24];
				end
			BYTE3: cur <= IDLE;
			default: cur <= IDLE;
		endcase
	end
	
	assign FD = fd_r;
	assign SLWRN = ~(BYTE0 <= cur && cur <= BYTE3);
	assign fifo_ready = FLAGN[1] & SLWRN;
	
endmodule
