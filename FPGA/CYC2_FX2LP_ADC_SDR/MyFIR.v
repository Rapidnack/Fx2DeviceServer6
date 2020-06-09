`timescale 1ns/1ns


module MyFIR
#(
	parameter RATE = 8,
	parameter DATA_WIDTH = 16
)
(
	input wire clk,
	input wire reset_n,
	
	input wire [4:0] gain,
	
	input wire signed [DATA_WIDTH-1:0] ast_sink_data,
	input wire ast_sink_valid,
	input wire [1:0] ast_sink_error,

	output reg signed [DATA_WIDTH-1:0] ast_source_data,
	output reg ast_source_valid,
	output wire [1:0] ast_source_error
);


	localparam NUM_TAPS = 39;
	localparam H_WIDTH = 16;
	localparam Y_WIDTH = H_WIDTH + DATA_WIDTH - 1;

	// RAM 16x256
	localparam RAM_NUM_ADDRS = 256;
	
	
	integer i;

	reg signed [H_WIDTH-1:0] h[0:NUM_TAPS-1];
	wire signed [DATA_WIDTH-1:0] x;
	reg signed [Y_WIDTH-1:0] y;
	
	wire [7:0] raddr;
	reg [7:0] waddr;
	reg [7:0] last_waddr;

	reg [8:0] cnt;
	reg [7:0] deci_cnt;

	
	// Normalized Frequency: 0.0625
	initial begin
		h[0] = 41;
		h[1] = 35;
		h[2] = 25;
		h[3] = 0;
		h[4] = -47;
		h[5] = -120;
		h[6] = -214;
		h[7] = -309;
		h[8] = -374;
		h[9] = -370;
		h[10] = -256;
		h[11] = 0;
		h[12] = 413;
		h[13] = 973;
		h[14] = 1641;
		h[15] = 2355;
		h[16] = 3034;
		h[17] = 3596;
		h[18] = 3966;
		h[19] = 4096;
		h[20] = 3966;
		h[21] = 3596;
		h[22] = 3034;
		h[23] = 2355;
		h[24] = 1641;
		h[25] = 973;
		h[26] = 413;
		h[27] = 0;
		h[28] = -256;
		h[29] = -370;
		h[30] = -374;
		h[31] = -309;
		h[32] = -214;
		h[33] = -120;
		h[34] = -47;
		h[35] = 0;
		h[36] = 25;
		h[37] = 35;
		h[38] = 41;
	end

	
	assign ast_source_error = 2'b00;
	
	always @(posedge clk)
	begin
		if (~reset_n) begin
			waddr <= 0;
			cnt <= 0;
			
			deci_cnt <= 0;
			
			y <= 0;
			ast_source_data <= 0;
			ast_source_valid <= 1'b0;
		end
		else begin
			
			if (ast_sink_valid) begin			
				if (waddr == RAM_NUM_ADDRS-1) begin
					waddr <= 0;
				end
				else begin
					waddr <= waddr + 1;			
				end
								
				if (deci_cnt == RATE-1) begin
					deci_cnt <= 0;
					
					ast_source_data <= y[Y_WIDTH-1-gain -: DATA_WIDTH];
					ast_source_valid <= 1'b1;
					
					last_waddr <= waddr;
					cnt <= 0;
					y <= 0;
				end
				else begin
					deci_cnt <= deci_cnt + 1;
					
					if (cnt < NUM_TAPS) begin
						cnt <= cnt + 1;
					end
					if (cnt < NUM_TAPS) begin
						y <= y + h[cnt] * x;
					end
					ast_source_valid <= 1'b0;
				end
			end
			else begin
				if (cnt < NUM_TAPS) begin
					cnt <= cnt + 1;
				end
				if (cnt < NUM_TAPS) begin
					y <= y + h[cnt] * x;
				end
				ast_source_valid <= 1'b0;
			end
		end
	end
	
	assign raddr = (RAM_NUM_ADDRS + last_waddr - (NUM_TAPS - 1) + cnt <= RAM_NUM_ADDRS-1)
					? RAM_NUM_ADDRS + last_waddr - (NUM_TAPS - 1) + cnt
					: RAM_NUM_ADDRS + last_waddr - (NUM_TAPS - 1) + cnt - RAM_NUM_ADDRS;

	ram16x256 ram16x256_inst (
		.clock (clk),
		.data (ast_sink_data),
		.rdaddress (raddr),
		.wraddress (waddr),
		.wren (ast_sink_valid),
		.q (x)
	);


endmodule
